using System.Text.Json;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class StoragePhaseDecisionTests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "isolated-test-recovery/storage-phases", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAtPreparedAndInstalledBoundariesRestoresAllPriorOutputs(bool afterInstallation)
    {
        var original = new AtomicSnapshotStorage(root);
        await original.CommitAsync(1, "{\"old\":true}", "{\"oldRich\":true}", null, true, true, SnapshotFixture.Now, default);
        var priorHistory = Directory.GetFiles(Path.Combine(root, "snapshots"), "*-snapshot-*.json");
        using var cancellation = new CancellationTokenSource();
        var boundaryReached = false;
        var operations = new Operations
        {
            Installed = destination =>
            {
                if (boundaryReached) return;
                if (afterInstallation)
                {
                    if (!Path.GetFileName(destination).StartsWith("automatic-snapshot-", StringComparison.Ordinal)) return;
                }
                else
                {
                    if (Path.GetFileName(destination) != "write-recovery.json") return;
                    using var journal = JsonDocument.Parse(File.ReadAllText(destination));
                    var rows = journal.RootElement.GetProperty("files");
                    if (rows.GetArrayLength() != 3 || !rows.EnumerateArray().All(row => File.Exists(Path.Combine(root, row.GetProperty("temporary").GetString()!)))) return;
                }
                boundaryReached = true; cancellation.Cancel();
            }
        };
        var storage = new AtomicSnapshotStorage(root, operations);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.CommitAsync(1, "{\"new\":true}", "{\"newRich\":true}", null, true, false, SnapshotFixture.Now.AddSeconds(1), cancellation.Token));

        Assert.True(boundaryReached);
        Assert.Equal("{\"old\":true}", await original.ReadAsync(1, false, default));
        Assert.Equal("{\"oldRich\":true}", await File.ReadAllTextAsync(original.CanonicalPath));
        Assert.Equal(priorHistory, Directory.GetFiles(Path.Combine(root, "snapshots"), "*-snapshot-*.json"));
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetentionJournalCanBeReadByNextInstanceAfterCleanupFailure(bool writeFirst)
    {
        var original = new AtomicSnapshotStorage(root);
        await original.CommitAsync(1, "{}", null, null, false, false, SnapshotFixture.Now, default);
        var folder = Path.Combine(root, "snapshots");
        for (var index = 0; index < 26; index++)
        {
            var path = Path.Combine(folder, $"manual-snapshot-20260905-000000-{index:000}.json");
            await File.WriteAllTextAsync(path, "{}");
            File.SetLastWriteTimeUtc(path, SnapshotFixture.Now.UtcDateTime);
        }
        var storage = new AtomicSnapshotStorage(root, new Operations
        {
            Deleting = path => { if (path.EndsWith(".bak", StringComparison.Ordinal)) throw new IOException("Keep the committed recovery journal for the next instance."); }
        });

        await storage.CommitAsync(1, "{\"accepted\":true}", "{\"rich\":true}", null, true, false, SnapshotFixture.Now, default);
        Assert.Equal("Pending", storage.RecoveryState);
        Assert.True(File.Exists(Path.Combine(root, "state", "write-recovery.json")));

        var recovered = new AtomicSnapshotStorage(root);
        if (writeFirst)
            await recovered.CommitAsync(1, "{\"accepted\":true}", null, null, false, false, SnapshotFixture.Now, default);
        Assert.Equal("{\"accepted\":true}", await recovered.ReadAsync(1, false, default));
        Assert.Equal(writeFirst ? "None" : "Recovered", recovered.RecoveryState);
        Assert.Equal(25, Directory.GetFiles(folder, "*-snapshot-*.json").Length);
        Assert.False(File.Exists(Path.Combine(folder, "manual-snapshot-20260905-000000-000.json")));
        Assert.False(File.Exists(Path.Combine(folder, "manual-snapshot-20260905-000000-001.json")));
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));

        await recovered.CommitAsync(1, "{}", null, null, true, false, SnapshotFixture.Now.AddSeconds(1), default);
        Assert.Equal(25, Directory.GetFiles(folder, "*-snapshot-*.json").Length);
        await recovered.CommitAsync(1, "{}", "{}", null, false, false, SnapshotFixture.Now.AddSeconds(2), default);
        Assert.Equal(25, Directory.GetFiles(folder, "*-snapshot-*.json").Length);
    }

    private sealed class Operations : IAtomicFileOperations
    {
        public Action<string>? Installed { get; init; }
        public Action<string>? Deleting { get; init; }
        public Stream OpenRead(string path) => new AtomicFileOperations().OpenRead(path);
        public void Move(string source, string destination) { File.Move(source, destination); Installed?.Invoke(destination); }
        public void Replace(string source, string destination, string? backup) { File.Replace(source, destination, backup); Installed?.Invoke(destination); }
        public void Delete(string path) { Deleting?.Invoke(path); File.Delete(path); }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
