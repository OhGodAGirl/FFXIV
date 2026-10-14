using System.Text;
using System.Text.Json;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class RecoveryLimitDecisionTests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "isolated-test-recovery/recovery-limits", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(65536, true)]
    [InlineData(65537, false)]
    public async Task RecoveryAcceptsExactlyItsUtf8ByteLimit(int bytes, bool accepted)
    {
        var storage = new AtomicSnapshotStorage(root);
        var journal = Journal(64);
        journal += new string(' ', bytes - Encoding.UTF8.GetByteCount(journal));
        await WriteJournal(journal);
        Assert.Equal(bytes, new FileInfo(Path.Combine(root, "state", "write-recovery.json")).Length);
        if (accepted)
        {
            Assert.Null(await storage.ReadAsync(1, false, default));
            Assert.Equal("Recovered", storage.RecoveryState);
            Assert.False(File.Exists(Path.Combine(root, "state", "write-recovery.json")));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => storage.ReadAsync(1, false, default));
            Assert.Equal("Failed", storage.RecoveryState);
            Assert.Equal(journal, await File.ReadAllTextAsync(Path.Combine(root, "state", "write-recovery.json")));
        }
    }

    [Theory]
    [InlineData(7, true)]
    [InlineData(8, false)]
    public async Task RecoveryEnforcesDepthIncludingUnknownProperties(int arrays, bool accepted)
    {
        var journal = Journal(1);
        journal = journal[..^1] + ",\"extension\":" + new string('[', arrays) + "0" + new string(']', arrays) + "}";
        await WriteJournal(journal);
        var storage = new AtomicSnapshotStorage(root);
        if (accepted)
        {
            Assert.Null(await storage.ReadAsync(1, false, default));
            Assert.Equal("Recovered", storage.RecoveryState);
        }
        else
        {
            await Assert.ThrowsAnyAsync<JsonException>(() => storage.ReadAsync(1, false, default));
            Assert.Equal("Failed", storage.RecoveryState);
            Assert.Equal(journal, await File.ReadAllTextAsync(Path.Combine(root, "state", "write-recovery.json")));
        }
    }

    [Fact]
    public async Task MaximumTransactionEntriesCommitAndPruneToTwentyFiveHistoryFiles()
    {
        var storage = new AtomicSnapshotStorage(root);
        await storage.CommitAsync(1, "{}", null, null, false, false, SnapshotFixture.Now, default);
        var folder = Path.Combine(root, "snapshots");
        for (var index = 0; index < 85; index++)
            await File.WriteAllTextAsync(Path.Combine(folder, $"manual-snapshot-20260905-000000-{index:000}.json"), "{}");

        // Three new outputs plus 61 obsolete history files produce exactly 64 journal entries.
        var result = await storage.CommitAsync(1, "{\"accepted\":true}", "{\"rich\":true}", null, true, false, SnapshotFixture.Now, default);

        Assert.Equal(25, result.HistoryCount);
        Assert.Equal(25, Directory.GetFiles(folder, "*-snapshot-*.json").Length);
        Assert.Equal("{\"accepted\":true}", await storage.ReadAsync(1, false, default));
        Assert.Equal("{\"rich\":true}", await File.ReadAllTextAsync(Path.Combine(root, "current-snapshot.json")));
        Assert.Equal("None", storage.RecoveryState);
    }

    private static string Journal(int count) => JsonSerializer.Serialize(new
    {
        format = "exportaramus-write-recovery",
        version = 1,
        committed = true,
        files = Enumerable.Range(1, count).Select(id =>
        {
            var destination = $"snapshots/characters/{id}/collection-data.json";
            var temporary = destination + "." + new string('a', 32) + ".tmp";
            return new { destination, temporary, backup = temporary + ".bak", existed = false, delete = false, applied = false };
        }).ToArray(),
    });

    private async Task WriteJournal(string json)
    {
        Directory.CreateDirectory(Path.Combine(root, "state"));
        await File.WriteAllTextAsync(Path.Combine(root, "state", "write-recovery.json"), json, new UTF8Encoding(false));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
