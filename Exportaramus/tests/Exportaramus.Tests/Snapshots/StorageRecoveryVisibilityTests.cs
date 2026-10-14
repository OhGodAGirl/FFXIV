using System.Text.Json;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class StorageRecoveryVisibilityTests
{
    [Fact]
    public async Task AnAlreadyCanceledCommitStopsBeforeValidatingInvalidInputOrOpeningAnyWriter()
    {
        var root = Root();
        try
        {
            var original = new AtomicSnapshotStorage(root);
            await original.CommitAsync(1, "{\"old\":true}", null, null, false, false, SnapshotFixture.Now, default);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var operations = new CountingOperations();
            var storage = new AtomicSnapshotStorage(root, operations);

            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.CommitAsync(1,
                "not JSON", null, null, false, false, SnapshotFixture.Now.AddSeconds(1), cancellation.Token));

            Assert.Equal(cancellation.Token, exception.CancellationToken);
            Assert.Equal(0, operations.WritersOpened);
            Assert.Equal("{\"old\":true}", await File.ReadAllTextAsync(storage.CharacterPath(1)));
            Assert.False(File.Exists(Path.Combine(root, "state", "write-recovery.json")));
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
            Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ANoJournalStartupReadReportsTheDefinedNoneRecoveryState()
    {
        var root = Root();
        try
        {
            var storage = new AtomicSnapshotStorage(root);
            Assert.Null(await storage.ReadAsync(1, false, default));
            Assert.Equal("NotFound", storage.Diagnostic.LastRead.Status);
            Assert.Equal("None", storage.RecoveryState);
            Assert.Equal("None", storage.Diagnostic.RecoveryState);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task FailedPreparationCleansItsJournalOrReportsThePendingRecoveryThatRemains(bool finalPreparation, bool cleanupFails)
    {
        var root = Root();
        try
        {
            var original = new AtomicSnapshotStorage(root);
            await original.CommitAsync(1, "{\"old\":true}", null, null, false, false, SnapshotFixture.Now, default);
            using var cancellation = new CancellationTokenSource();
            var operations = new Operations(root, cancellation, finalPreparation, cleanupFails);
            var storage = new AtomicSnapshotStorage(root, operations);

            var exception = await Record.ExceptionAsync(() => storage.CommitAsync(1, "{\"new\":true}", null, null,
                false, false, SnapshotFixture.Now.AddSeconds(1), cancellation.Token));

            Assert.True(operations.FailureBoundaryReached);
            Assert.True(operations.TemporaryCleanupAttempted);
            if (cleanupFails) Assert.IsType<UnauthorizedAccessException>(exception);
            else if (finalPreparation) Assert.IsAssignableFrom<OperationCanceledException>(exception);
            else Assert.IsType<IOException>(exception);
            Assert.Equal("{\"old\":true}", await File.ReadAllTextAsync(storage.CharacterPath(1)));
            Assert.Equal("Failed", storage.Diagnostic.LastWrite.Status);
            Assert.Equal("WriteFailed", storage.Diagnostic.LastWrite.FailureCode);
            var journal = Path.Combine(root, "state", "write-recovery.json");
            Assert.Equal(cleanupFails, File.Exists(journal));
            Assert.Equal(cleanupFails ? "Pending" : "None", storage.RecoveryState);
            Assert.Equal(cleanupFails ? "Pending" : "None", storage.Diagnostic.RecoveryState);
            if (cleanupFails)
            {
                using var record = JsonDocument.Parse(await File.ReadAllTextAsync(journal));
                Assert.False(record.RootElement.GetProperty("committed").GetBoolean());
                Assert.Single(record.RootElement.GetProperty("files").EnumerateArray());
            }

            var next = new AtomicSnapshotStorage(root);
            Assert.Equal("{\"old\":true}", await next.ReadAsync(1, false, default));
            Assert.Equal(cleanupFails ? "Recovered" : "None", next.Diagnostic.RecoveryState);
            Assert.False(File.Exists(journal));
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
            Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static string Root() => Path.Combine(AppContext.BaseDirectory, "atomic-recovery-visibility", Guid.NewGuid().ToString("N"));

    private sealed class CountingOperations : IAtomicFileOperations
    {
        public int WritersOpened { get; private set; }
        public Stream OpenRead(string path) => new AtomicFileOperations().OpenRead(path);
        public FileStream OpenWrite(string path, FileMode mode, int bufferSize, FileOptions options)
        {
            WritersOpened++;
            return new(path, mode, FileAccess.Write, FileShare.None, bufferSize, options);
        }
        public void Move(string source, string destination) => File.Move(source, destination);
        public void Replace(string source, string destination, string? backup) => File.Replace(source, destination, backup);
        public void Delete(string path) => File.Delete(path);
    }

    private sealed class Operations(string root, CancellationTokenSource cancellation, bool finalPreparation, bool cleanupFails) : IAtomicFileOperations
    {
        public bool FailureBoundaryReached { get; private set; }
        public bool TemporaryCleanupAttempted { get; private set; }
        public Stream OpenRead(string path) => new AtomicFileOperations().OpenRead(path);
        public FileStream OpenWrite(string path, FileMode mode, int bufferSize, FileOptions options) =>
            new Writer(path, mode, bufferSize, options, () =>
            {
                if (!finalPreparation && mode == FileMode.CreateNew)
                { FailureBoundaryReached = true; throw new IOException("Temporary payload could not be durably flushed."); }
            });
        public void Move(string source, string destination) { File.Move(source, destination); Installed(destination); }
        public void Replace(string source, string destination, string? backup) { File.Replace(source, destination, backup); Installed(destination); }
        public void Delete(string path)
        {
            if (path.EndsWith(".tmp", StringComparison.Ordinal))
            {
                TemporaryCleanupAttempted = true;
                if (cleanupFails) throw new UnauthorizedAccessException("Named temporary could not be removed.");
            }
            File.Delete(path);
        }
        private void Installed(string destination)
        {
            if (!finalPreparation || FailureBoundaryReached || Path.GetFileName(destination) != "write-recovery.json") return;
            using var record = JsonDocument.Parse(File.ReadAllText(destination));
            var rows = record.RootElement.GetProperty("files");
            if (rows.GetArrayLength() != 1 || !File.Exists(Path.Combine(root, rows[0].GetProperty("temporary").GetString()!))) return;
            FailureBoundaryReached = true;
            cancellation.Cancel();
        }
    }

    private sealed class Writer(string path, FileMode mode, int bufferSize, FileOptions options, Action beforeDurability)
        : FileStream(path, mode, FileAccess.Write, FileShare.None, bufferSize, options)
    {
        public override void Flush(bool flushToDisk)
        {
            if (flushToDisk) beforeDurability();
            base.Flush(flushToDisk);
        }
    }
}
