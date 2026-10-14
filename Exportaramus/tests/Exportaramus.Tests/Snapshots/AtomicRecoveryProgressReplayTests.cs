using System.Text.Json;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class AtomicRecoveryProgressReplayTests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "isolated-test-recovery/progress-replay", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedRestoresAreNotReplayedWhenOnlyPendingFilesBecomeAvailable(bool unauthorized)
    {
        var original = new AtomicSnapshotStorage(root);
        await original.CommitAsync(1, "{\"old\":true}", "{\"oldRich\":true}", "{\"oldReport\":true}", false, false, SnapshotFixture.Now, default);
        var phase = 0;
        var completedFileRetries = 0;
        var operations = new Operations
        {
            Moving = (_, destination) =>
            {
                if (phase == 0 && Path.GetFileName(destination).StartsWith("automatic-snapshot-", StringComparison.Ordinal))
                    throw new IOException("History installation is unavailable.");
            },
            Copying = (_, destination) =>
            {
                var compact = Path.GetFullPath(destination).StartsWith(original.CharacterPath(1) + ".", StringComparison.Ordinal);
                var evidence = Path.GetFullPath(destination).StartsWith(original.EvidencePath(1) + ".", StringComparison.Ordinal);
                if ((phase < 2 && compact) || (phase == 0 && evidence))
                    throw new IOException("This restore is still unavailable.");
                if (phase == 2 && evidence)
                {
                    completedFileRetries++;
                    if (unauthorized) throw new UnauthorizedAccessException("The already restored evidence file is unavailable again.");
                    throw new IOException("The already restored evidence file is unavailable again.");
                }
            }
        };
        var storage = new AtomicSnapshotStorage(root, operations);
        await Assert.ThrowsAsync<IOException>(() => storage.CommitAsync(1, "{\"new\":true}", "{\"newRich\":true}", "{\"newReport\":true}", true, false, SnapshotFixture.Now.AddSeconds(1), default));
        Assert.Equal("{\"oldRich\":true}", await File.ReadAllTextAsync(storage.CanonicalPath));

        phase = 1;
        await Assert.ThrowsAsync<IOException>(() => storage.ReadAsync(1, false, default));
        Assert.Equal("{\"oldReport\":true}", await File.ReadAllTextAsync(storage.EvidencePath(1)));
        Assert.Equal("{\"new\":true}", await File.ReadAllTextAsync(storage.CharacterPath(1)));
        Assert.Equal("Failed", storage.Diagnostic.RecoveryState);
        var journalPath = Path.Combine(root, "state", "write-recovery.json");
        using (var journal = JsonDocument.Parse(await File.ReadAllTextAsync(journalPath)))
        {
            var pending = journal.RootElement.GetProperty("files").EnumerateArray().Where(row => row.GetProperty("applied").GetBoolean());
            Assert.EndsWith("collection-data.json", Assert.Single(pending).GetProperty("destination").GetString());
        }
        var retainedEvidenceBackup = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(storage.EvidencePath(1))!, "reported-evidence.json.*.tmp.bak"));
        Assert.Equal("{\"oldReport\":true}", await File.ReadAllTextAsync(retainedEvidenceBackup));

        // Only the unfinished compact restore becomes available; completed evidence must not be copied again.
        phase = 2;
        Assert.Equal("{\"old\":true}", await storage.ReadAsync(1, false, default));
        Assert.Equal(0, completedFileRetries);
        Assert.Equal("{\"oldReport\":true}", await storage.ReadAsync(1, true, default));
        Assert.Equal("{\"oldRich\":true}", await File.ReadAllTextAsync(storage.CanonicalPath));
        Assert.Equal("Recovered", storage.Diagnostic.RecoveryState);
        Assert.False(File.Exists(journalPath));
        Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "snapshots"), "*-snapshot-*.json"));
    }

    private sealed class Operations : IAtomicFileOperations
    {
        public Action<string, string>? Moving { get; init; }
        public Action<string, string>? Copying { get; init; }
        public Stream OpenRead(string path) => new AtomicFileOperations().OpenRead(path);
        public void Copy(string source, string destination, bool overwrite) { Copying?.Invoke(source, destination); File.Copy(source, destination, overwrite); }
        public void Move(string source, string destination) { Moving?.Invoke(source, destination); File.Move(source, destination); }
        public void Replace(string source, string destination, string? backup) => File.Replace(source, destination, backup);
        public void Delete(string path) => File.Delete(path);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
