using System.Text.Json;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class AtomicBestEffortRecoveryTests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "isolated-test-recovery/best-effort", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableHistoryUndoDoesNotLeaveAvailableCanonicalAtRejectedBytes(bool unauthorized)
    {
        var original = new AtomicSnapshotStorage(root);
        await original.CommitAsync(1, "{\"old\":true}", "{\"oldRich\":true}", null, false, false, SnapshotFixture.Now, default);
        using var cancellation = new CancellationTokenSource();
        var unavailable = true;
        var operations = new Operations
        {
            Installed = destination => { if (IsHistory(destination)) cancellation.Cancel(); },
            Deleting = path =>
            {
                if (unavailable && IsHistory(path))
                {
                    if (unauthorized) throw new UnauthorizedAccessException("history undo unavailable");
                    throw new IOException("history undo unavailable");
                }
            },
        };
        var storage = new AtomicSnapshotStorage(root, operations);
        var commit = storage.CommitAsync(1, "{\"new\":true}", "{\"newRich\":true}", null, true, false, SnapshotFixture.Now.AddSeconds(1), cancellation.Token);
        if (unauthorized) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => commit);
        else await Assert.ThrowsAsync<IOException>(() => commit);
        Assert.Equal("{\"oldRich\":true}", await File.ReadAllTextAsync(storage.CanonicalPath));
        Assert.Equal("{\"old\":true}", await File.ReadAllTextAsync(storage.CharacterPath(1)));
        Assert.Equal("Failed", storage.Diagnostic.RecoveryState);
        using (var journal = JsonDocument.Parse(await File.ReadAllTextAsync(Journal)))
        {
            var pending = journal.RootElement.GetProperty("files").EnumerateArray().Where(row => row.GetProperty("applied").GetBoolean()).ToArray();
            Assert.True(IsHistory(Assert.Single(pending).GetProperty("destination").GetString()!));
        }
        unavailable = false;
        Assert.Equal("{\"old\":true}", await storage.ReadAsync(1, false, default));
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "snapshots"), "*-snapshot-*.json"));
        Assert.False(File.Exists(Journal));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RepeatedPartialRecoveryPreservesBackupsEvenWhenProgressJournalCannotBeWritten(bool progressWriteFails, bool unauthorized)
    {
        var original = new AtomicSnapshotStorage(root);
        await original.CommitAsync(1, "{\"old\":true}", "{\"oldRich\":true}", "{\"oldReport\":true}", false, false, SnapshotFixture.Now, default);
        var phase = 0;
        var operations = new Operations
        {
            Moving = (_, destination) => { if (phase == 0 && IsHistory(destination)) throw new IOException("history installation unavailable"); },
            Copying = (_, destination) =>
            {
                var compact = Path.GetFullPath(destination).StartsWith(original.CharacterPath(1) + ".", StringComparison.Ordinal);
                var evidence = Path.GetFullPath(destination).StartsWith(original.EvidencePath(1) + ".", StringComparison.Ordinal);
                if ((phase < 2 && compact) || (phase == 0 && evidence))
                {
                    if (unauthorized) throw new UnauthorizedAccessException("rollback copy unavailable");
                    throw new IOException("rollback copy unavailable");
                }
            },
            Opening = (path, _) =>
            {
                if (phase == 1 && progressWriteFails && Path.GetFullPath(path) == Path.GetFullPath(Journal + ".tmp")) throw new IOException("progress journal unavailable");
            },
        };
        var storage = new AtomicSnapshotStorage(root, operations);
        var first = storage.CommitAsync(1, "{\"new\":true}", "{\"newRich\":true}", "{\"newReport\":true}", true, false, SnapshotFixture.Now.AddSeconds(1), default);
        if (unauthorized) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => first);
        else await Assert.ThrowsAsync<IOException>(() => first);
        Assert.Equal("{\"oldRich\":true}", await File.ReadAllTextAsync(storage.CanonicalPath));
        var priorJournal = await File.ReadAllBytesAsync(Journal);
        phase = 1;
        var second = storage.ReadAsync(1, false, default);
        if (unauthorized && !progressWriteFails) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => second);
        else await Assert.ThrowsAsync<IOException>(() => second);
        Assert.Equal("{\"oldReport\":true}", await File.ReadAllTextAsync(storage.EvidencePath(1)));
        Assert.Equal("Failed", storage.Diagnostic.RecoveryState);
        var evidenceBackup = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(storage.EvidencePath(1))!, "reported-evidence.json.*.tmp.bak"));
        Assert.Equal("{\"oldReport\":true}", await File.ReadAllTextAsync(evidenceBackup));
        if (progressWriteFails) Assert.Equal(priorJournal, await File.ReadAllBytesAsync(Journal));
        else
        {
            using var journal = JsonDocument.Parse(await File.ReadAllTextAsync(Journal));
            var pending = journal.RootElement.GetProperty("files").EnumerateArray().Where(row => row.GetProperty("applied").GetBoolean()).ToArray();
            Assert.EndsWith("collection-data.json", Assert.Single(pending).GetProperty("destination").GetString());
        }
        phase = 2;
        Assert.Equal("{\"old\":true}", await storage.ReadAsync(1, false, default));
        Assert.Equal("{\"oldReport\":true}", await storage.ReadAsync(1, true, default));
        Assert.Equal("{\"oldRich\":true}", await File.ReadAllTextAsync(storage.CanonicalPath));
        Assert.Equal("Recovered", storage.Diagnostic.RecoveryState);
        Assert.False(File.Exists(Journal));
        Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CompletedRollbackRecordsProgressBeforeCleanupCanConsumeBackups(bool duringRecovery, bool progressWriteFails)
    {
        var original = new AtomicSnapshotStorage(root);
        await original.CommitAsync(1, "{\"old\":true}", "{\"oldRich\":true}", null, false, false, SnapshotFixture.Now, default);
        var phase = 0;
        var rollback = false;
        var backupDeletes = 0;
        var operations = new Operations
        {
            Moving = (_, destination) =>
            {
                if (phase == 0 && IsHistory(destination)) { rollback = true; throw new IOException("installation unavailable"); }
            },
            Copying = (_, destination) =>
            {
                if (duringRecovery && phase == 0 && Path.GetFullPath(destination).StartsWith(original.CharacterPath(1) + ".", StringComparison.Ordinal))
                    throw new IOException("initial partial rollback");
            },
            Opening = (path, _) =>
            {
                if (rollback && phase == (duringRecovery ? 1 : 0) && progressWriteFails && Path.GetFullPath(path) == Path.GetFullPath(Journal + ".tmp"))
                    throw new IOException("terminal progress unavailable");
            },
            Deleting = path =>
            {
                if (phase == (duringRecovery ? 1 : 0) && path.EndsWith(".bak", StringComparison.Ordinal) && ++backupDeletes == 2)
                    throw new IOException("cleanup unavailable after consuming one backup");
            },
        };
        var storage = new AtomicSnapshotStorage(root, operations);
        await Assert.ThrowsAsync<IOException>(() => storage.CommitAsync(1, "{\"new\":true}", "{\"newRich\":true}", null, true, false, SnapshotFixture.Now.AddSeconds(1), default));
        if (duringRecovery)
        {
            phase = 1;
            await Assert.ThrowsAsync<IOException>(() => storage.ReadAsync(1, false, default));
        }
        Assert.Equal("{\"oldRich\":true}", await File.ReadAllTextAsync(storage.CanonicalPath));
        Assert.Equal("{\"old\":true}", await File.ReadAllTextAsync(storage.CharacterPath(1)));
        Assert.True(File.Exists(Journal));
        Assert.Equal(progressWriteFails ? 0 : 2, backupDeletes);
        if (progressWriteFails) Assert.Equal(2, Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories).Length);
        else
        {
            using var journal = JsonDocument.Parse(await File.ReadAllTextAsync(Journal));
            Assert.True(journal.RootElement.GetProperty("rollbackFailed").GetBoolean());
            Assert.All(journal.RootElement.GetProperty("files").EnumerateArray(), row => Assert.False(row.GetProperty("applied").GetBoolean()));
        }
        phase = 2;
        Assert.Equal("{\"old\":true}", await storage.ReadAsync(1, false, default));
        Assert.Equal("{\"oldRich\":true}", await File.ReadAllTextAsync(storage.CanonicalPath));
        Assert.Equal("Recovered", storage.Diagnostic.RecoveryState);
        Assert.False(File.Exists(Journal));
        Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
    }

    private string Journal => Path.Combine(root, "state", "write-recovery.json");
    [Fact]
    public async Task DeliberatelyDeletedEvidenceRemainsOmittedAcrossPartialRecoveryCleanup()
    {
        var original = new AtomicSnapshotStorage(root);
        await original.CommitAsync(1, "{}", "{\"old\":true}", "{\"oldReport\":true}", false, false, SnapshotFixture.Now, default);
        var phase = 0;
        var operations = new Operations
        {
            Moving = (_, destination) => { if (phase == 0 && IsHistory(destination)) throw new IOException("installation unavailable"); },
            Copying = (_, destination) =>
            {
                if (phase == 0 && Path.GetFullPath(destination).StartsWith(Path.GetDirectoryName(original.CharacterPath(1))!, StringComparison.Ordinal))
                    throw new IOException("initial character restore unavailable");
            },
            Deleting = path =>
            {
                if (phase == 1 && path.EndsWith(".bak", StringComparison.Ordinal) && Path.GetFullPath(path).StartsWith(original.CanonicalPath + ".", StringComparison.Ordinal))
                    throw new IOException("cleanup unavailable after evidence backup consumed");
            },
        };
        var storage = new AtomicSnapshotStorage(root, operations);
        await Assert.ThrowsAsync<IOException>(() => storage.CommitAsync(1, "{\"new\":true}", "{\"new\":true}", "{\"newReport\":true}", true, false, SnapshotFixture.Now.AddSeconds(1), default));
        File.Delete(original.EvidencePath(1));
        phase = 1;
        await Assert.ThrowsAsync<IOException>(() => storage.ReadAsync(1, true, default));
        Assert.False(File.Exists(original.EvidencePath(1)));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(original.EvidencePath(1))!, "reported-evidence.json.*.tmp.bak"));
        Assert.True(File.Exists(Journal));
        phase = 2;
        Assert.Null(await storage.ReadAsync(1, true, default));
        Assert.Equal("{}", await storage.ReadAsync(1, false, default));
        Assert.Equal("{\"old\":true}", await File.ReadAllTextAsync(storage.CanonicalPath));
        Assert.Equal("Recovered", storage.Diagnostic.RecoveryState);
        Assert.False(File.Exists(Journal));
        Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestoredTemporaryIsDurablyFlushedAndClosedBeforeInstallation(bool flushFails)
    {
        var original = new AtomicSnapshotStorage(root);
        await original.CommitAsync(1, "{}", "{\"old\":true}", null, false, false, SnapshotFixture.Now, default);
        var failing = flushFails;
        var operations = new Operations
        {
            Moving = (_, destination) => { if (IsHistory(destination)) throw new IOException("history installation unavailable"); },
            RestoreFlushFails = () => failing,
        };
        var storage = new AtomicSnapshotStorage(root, operations);
        await Assert.ThrowsAsync<IOException>(() => storage.CommitAsync(1, "{\"new\":true}", "{\"new\":true}", null, true, false, SnapshotFixture.Now.AddSeconds(1), default));
        Assert.Equal(2, operations.RestoreStreams.Count);
        Assert.All(operations.RestoreStreams, stream =>
        {
            Assert.Equal(FileOptions.WriteThrough, stream.Options);
            Assert.True(stream.HandleClosed);
            Assert.Equal(!flushFails, stream.FlushedToDisk);
            Assert.Equal(!flushFails, stream.Installed);
        });
        if (flushFails)
        {
            Assert.Equal(2, Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories).Length);
            Assert.True(File.Exists(Journal));
            failing = false;
            Assert.Equal("{}", await storage.ReadAsync(1, false, default));
        }
        Assert.Equal("{\"old\":true}", await File.ReadAllTextAsync(storage.CanonicalPath));
        Assert.False(File.Exists(Journal));
    }

    [Fact]
    public async Task MultipleRollbackFailuresPreserveTheFirstExceptionTypeAndStillRestoreCanonical()
    {
        var original = new AtomicSnapshotStorage(root);
        await original.CommitAsync(1, "{}", "{\"old\":true}", null, false, false, SnapshotFixture.Now, default);
        using var cancellation = new CancellationTokenSource();
        var unavailable = true;
        var operations = new Operations
        {
            Installed = destination => { if (IsHistory(destination)) cancellation.Cancel(); },
            Deleting = path => { if (unavailable && IsHistory(path)) throw new IOException(); },
            Copying = (_, destination) =>
            {
                if (unavailable && Path.GetFullPath(destination).StartsWith(original.CharacterPath(1) + ".", StringComparison.Ordinal))
                    throw new UnauthorizedAccessException();
            },
        };
        var storage = new AtomicSnapshotStorage(root, operations);
        await Assert.ThrowsAsync<IOException>(() => storage.CommitAsync(1, "{\"new\":true}", "{\"new\":true}", null, true, false, SnapshotFixture.Now.AddSeconds(1), cancellation.Token));
        Assert.Equal("{\"old\":true}", await File.ReadAllTextAsync(storage.CanonicalPath));
        unavailable = false;
        Assert.Equal("{}", await storage.ReadAsync(1, false, default));
    }

    private static bool IsHistory(string path) => Path.GetFileName(path).StartsWith("automatic-snapshot-", StringComparison.Ordinal) && path.EndsWith(".json", StringComparison.Ordinal);
    private sealed class Operations : IAtomicFileOperations
    {
        public Action<string>? Installed { get; init; }
        public Action<string>? Deleting { get; init; }
        public Action<string, string>? Moving { get; init; }
        public Action<string, string>? Copying { get; init; }
        public Action<string, FileMode>? Opening { get; init; }
        public Func<bool>? RestoreFlushFails { get; init; }
        public List<RestoreStream> RestoreStreams { get; } = [];
        public Stream OpenRead(string path) => new AtomicFileOperations().OpenRead(path);
        public FileStream OpenWrite(string path, FileMode mode, int bufferSize, FileOptions options)
        {
            Opening?.Invoke(path, mode);
            if (mode != FileMode.Open) return new(path, mode, FileAccess.Write, FileShare.None, bufferSize, options);
            var stream = new RestoreStream(path, options, RestoreFlushFails?.Invoke() ?? false);
            RestoreStreams.Add(stream);
            return stream;
        }
        public void Copy(string source, string destination, bool overwrite) { Copying?.Invoke(source, destination); File.Copy(source, destination, overwrite); }
        public void Move(string source, string destination) { Moving?.Invoke(source, destination); VerifyRestore(source); File.Move(source, destination); Installed?.Invoke(destination); }
        public void Replace(string source, string destination, string? backup) { VerifyRestore(source); File.Replace(source, destination, backup); Installed?.Invoke(destination); }
        public void Delete(string path) { Deleting?.Invoke(path); File.Delete(path); }
        private void VerifyRestore(string source)
        {
            var stream = RestoreStreams.LastOrDefault(value => value.Path == source);
            if (stream is null) return;
            Assert.True(stream.HandleClosed);
            Assert.True(stream.FlushedToDisk);
            stream.Installed = true;
        }
    }
    private sealed class RestoreStream : FileStream
    {
        private readonly bool fail;
        private readonly Microsoft.Win32.SafeHandles.SafeFileHandle capturedHandle;
        public string Path { get; }
        public FileOptions Options { get; }
        public bool FlushedToDisk { get; private set; }
        public bool Installed { get; set; }
        public bool HandleClosed => capturedHandle.IsClosed;
        public RestoreStream(string path, FileOptions options, bool fail) : base(path, FileMode.Open, FileAccess.Write, FileShare.None, 8192, options)
        { Path = path; Options = options; capturedHandle = SafeFileHandle; this.fail = fail; }
        public override void Flush(bool flushToDisk)
        {
            if (fail) throw new IOException("restoration flush unavailable");
            base.Flush(flushToDisk);
            FlushedToDisk = flushToDisk;
        }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
