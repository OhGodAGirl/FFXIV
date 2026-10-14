using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class AtomicCanceledPreparationTests
{
    [Fact]
    public async Task CancellationBeforePreparationCreatesNoRecoveryRecordAndAllowsSameInstanceRetry()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "isolated-test-recovery/canceled-preparation", Guid.NewGuid().ToString("N"));
        var original = new AtomicSnapshotStorage(root);
        try
        {
            await original.CommitAsync(1, "{\"old\":true}", "{\"oldRich\":true}", null, false, false, SnapshotFixture.Now, default);
            var operations = new Operations { RecoveryDeletionUnavailable = true };
            var storage = new AtomicSnapshotStorage(root, operations);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.CommitAsync(1, "{\"new\":true}", "{\"newRich\":true}", null, false, false, SnapshotFixture.Now.AddSeconds(1), cancellation.Token));

            Assert.False(File.Exists(Path.Combine(root, "state", "write-recovery.json")));
            Assert.Equal("{\"old\":true}", await File.ReadAllTextAsync(storage.CharacterPath(1)));
            Assert.Equal("{\"oldRich\":true}", await File.ReadAllTextAsync(storage.CanonicalPath));
            Assert.Equal("None", storage.Diagnostic.RecoveryState);
            operations.RecoveryDeletionUnavailable = false;
            await storage.CommitAsync(1, "{\"retry\":true}", "{\"retryRich\":true}", null, false, false, SnapshotFixture.Now.AddSeconds(2), default);
            Assert.Equal("{\"retry\":true}", await storage.ReadAsync(1, false, default));
            Assert.Equal("{\"retryRich\":true}", await File.ReadAllTextAsync(storage.CanonicalPath));
            Assert.False(File.Exists(Path.Combine(root, "state", "write-recovery.json")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class Operations : IAtomicFileOperations
    {
        public bool RecoveryDeletionUnavailable { get; set; }
        public Stream OpenRead(string path) => new AtomicFileOperations().OpenRead(path);
        public void Move(string source, string destination) => File.Move(source, destination);
        public void Replace(string source, string destination, string? backup) => File.Replace(source, destination, backup);
        public void Delete(string path)
        {
            if (RecoveryDeletionUnavailable && Path.GetFileName(path) == "write-recovery.json") throw new IOException("Recovery record deletion temporarily unavailable.");
            File.Delete(path);
        }
    }
}
