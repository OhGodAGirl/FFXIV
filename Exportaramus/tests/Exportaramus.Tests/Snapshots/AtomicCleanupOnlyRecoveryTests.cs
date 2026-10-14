using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class AtomicCleanupOnlyRecoveryTests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "isolated-test-recovery/cleanup-only", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupOnlyRecoveryRetainsAcceptedBytesWithoutRequiringAnotherJournalWrite(bool afterRestoration)
    {
        var original = new AtomicSnapshotStorage(root);
        await original.CommitAsync(1, "{\"old\":true}", "{\"oldRich\":true}", null, false, false, SnapshotFixture.Now, default);
        var retry = false;
        var payloads = 0;
        using var cancellation = new CancellationTokenSource();
        var operations = new Operations
        {
            Opening = (path, mode) =>
            {
                if (retry && Path.GetFileName(path) == "write-recovery.json.tmp") throw new IOException("Journal writes remain unavailable while cleanup can proceed.");
                if (!retry && !afterRestoration && mode == FileMode.CreateNew && ++payloads == 2) throw new IOException("Payload preparation unavailable.");
            },
            Installed = destination =>
            {
                if (!retry && afterRestoration && Path.GetFileName(destination).StartsWith("automatic-snapshot-", StringComparison.Ordinal)) cancellation.Cancel();
            },
            Deleting = path =>
            {
                if (!retry && (afterRestoration ? path.EndsWith(".bak", StringComparison.Ordinal) : path.EndsWith(".tmp", StringComparison.Ordinal))) throw new IOException("Cleanup temporarily unavailable.");
            },
        };
        var storage = new AtomicSnapshotStorage(root, operations);
        await Assert.ThrowsAsync<IOException>(() => storage.CommitAsync(1, "{\"new\":true}", "{\"newRich\":true}", null, true, false, SnapshotFixture.Now.AddSeconds(1), cancellation.Token));
        Assert.True(File.Exists(Path.Combine(root, "state", "write-recovery.json")));
        Assert.Equal("{\"oldRich\":true}", await File.ReadAllTextAsync(storage.CanonicalPath));
        retry = true;

        Assert.Equal("{\"old\":true}", await storage.ReadAsync(1, false, default));

        Assert.Equal("{\"oldRich\":true}", await File.ReadAllTextAsync(storage.CanonicalPath));
        Assert.Equal("Succeeded", storage.Diagnostic.LastRead.Status);
        Assert.Equal("Recovered", storage.Diagnostic.RecoveryState);
        Assert.False(File.Exists(Path.Combine(root, "state", "write-recovery.json")));
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CompletedEvidenceOmissionNeedsOnlyCleanupOnTheNextRecovery()
    {
        var original = new AtomicSnapshotStorage(root);
        await original.CommitAsync(1, "{\"old\":true}", "{\"oldRich\":true}", "{\"oldReport\":true}", false, false, SnapshotFixture.Now, default);
        var phase = 0;
        var operations = new Operations
        {
            Moving = (_, destination) => { if (phase == 0 && Path.GetFileName(destination).StartsWith("automatic-snapshot-", StringComparison.Ordinal)) throw new IOException("History installation unavailable."); },
            Copying = (_, destination) =>
            {
                if (phase == 0 && Path.GetFullPath(destination).StartsWith(Path.GetDirectoryName(original.CharacterPath(1))!, StringComparison.Ordinal)) throw new IOException("Initial character restore unavailable.");
            },
            Deleting = path => { if (phase == 1 && path.EndsWith(".bak", StringComparison.Ordinal)) throw new IOException("Cleanup temporarily unavailable."); },
            Opening = (path, _) => { if (phase == 2 && Path.GetFileName(path) == "write-recovery.json.tmp") throw new IOException("Journal writes unavailable while cleanup can proceed."); },
        };
        await Assert.ThrowsAsync<IOException>(() => new AtomicSnapshotStorage(root, operations).CommitAsync(1, "{\"new\":true}", "{\"newRich\":true}", "{\"newReport\":true}", true, false, SnapshotFixture.Now.AddSeconds(1), default));
        File.Delete(original.EvidencePath(1));
        phase = 1;
        var recovered = new AtomicSnapshotStorage(root, operations);
        await Assert.ThrowsAsync<IOException>(() => recovered.ReadAsync(1, true, default));
        Assert.True(File.Exists(Path.Combine(root, "state", "write-recovery.json")));
        phase = 2;

        Assert.Null(await recovered.ReadAsync(1, true, default));

        Assert.Equal("{\"old\":true}", await recovered.ReadAsync(1, false, default));
        Assert.Equal("{\"oldRich\":true}", await File.ReadAllTextAsync(recovered.CanonicalPath));
        Assert.Equal("Recovered", recovered.Diagnostic.RecoveryState);
        Assert.False(File.Exists(recovered.EvidencePath(1)));
        Assert.False(File.Exists(Path.Combine(root, "state", "write-recovery.json")));
    }

    private sealed class Operations : IAtomicFileOperations
    {
        public Action<string, FileMode>? Opening { get; init; }
        public Action<string, string>? Moving { get; init; }
        public Action<string>? Installed { get; init; }
        public Action<string, string>? Copying { get; init; }
        public Action<string>? Deleting { get; init; }
        public Stream OpenRead(string path) => new AtomicFileOperations().OpenRead(path);
        public FileStream OpenWrite(string path, FileMode mode, int bufferSize, FileOptions options)
        { Opening?.Invoke(path, mode); return new(path, mode, FileAccess.Write, FileShare.None, bufferSize, options); }
        public void Move(string source, string destination) { Moving?.Invoke(source, destination); File.Move(source, destination); Installed?.Invoke(destination); }
        public void Copy(string source, string destination, bool overwrite) { Copying?.Invoke(source, destination); File.Copy(source, destination, overwrite); }
        public void Replace(string source, string destination, string? backup) => File.Replace(source, destination, backup);
        public void Delete(string path) { Deleting?.Invoke(path); File.Delete(path); }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
