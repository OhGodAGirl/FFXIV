using System.Text.Json;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class SnapshotRecoveryMutationTests
{
    [Fact]
    public async Task SuccessfulNoJournalCheckIsCachedForTheStorageInstance()
    {
        var directory = NewDirectory();
        var storage = new AtomicSnapshotStorage(directory);
        try
        {
            Assert.Null(await storage.ReadAsync(123, false, default));
            Assert.Equal("None", storage.Diagnostic.RecoveryState);
            var journal = WriteJournal(directory, "malformed journal written after the initial check");
            var sentinel = await File.ReadAllBytesAsync(journal);

            Assert.Null(await storage.ReadAsync(123, false, default));
            Assert.Equal("NotFound", storage.Diagnostic.LastRead.Status);
            Assert.Equal("None", storage.Diagnostic.RecoveryState);
            Assert.Equal(sentinel, await File.ReadAllBytesAsync(journal));
        }
        finally { RemoveDirectory(directory); }
    }

    [Fact]
    public async Task SuccessfulRecoveryCheckIsCachedForTheStorageInstance()
    {
        var directory = NewDirectory();
        var storage = new AtomicSnapshotStorage(directory);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(storage.CharacterPath(123))!);
            await File.WriteAllTextAsync(storage.CharacterPath(123), "{\"accepted\":true}");
            const string destination = "snapshots/characters/123/collection-data.json";
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var journal = WriteJournal(directory, JsonSerializer.Serialize(new
            {
                format = "exportaramus-write-recovery",
                version = 1,
                committed = true,
                rollbackFailed = false,
                files = new[] { new { destination, temporary, backup = temporary + ".bak", existed = false, delete = false, applied = true } },
            }));
            Assert.Equal("{\"accepted\":true}", await storage.ReadAsync(123, false, default));
            Assert.Equal("Recovered", storage.Diagnostic.RecoveryState);
            Assert.False(File.Exists(journal));
            WriteJournal(directory, "malformed journal written after completed recovery");
            var sentinel = await File.ReadAllBytesAsync(journal);

            Assert.Equal("{\"accepted\":true}", await storage.ReadAsync(123, false, default));
            Assert.Equal("Succeeded", storage.Diagnostic.LastRead.Status);
            Assert.Equal("Recovered", storage.Diagnostic.RecoveryState);
            Assert.Equal(sentinel, await File.ReadAllBytesAsync(journal));
        }
        finally { RemoveDirectory(directory); }
    }

    [Fact]
    public async Task PreparationFailurePreservesARecoveryJournalItDidNotWrite()
    {
        var directory = NewDirectory();
        var storage = new AtomicSnapshotStorage(directory);
        try
        {
            Assert.Null(await storage.ReadAsync(123, false, default));
            var journal = WriteJournal(directory, "unowned journal written after the initial check");
            var sentinel = await File.ReadAllBytesAsync(journal);

            await Assert.ThrowsAnyAsync<JsonException>(() => storage.CommitAsync(123, "invalid JSON", null, null,
                false, false, SnapshotFixture.Now, default));

            Assert.True(File.Exists(journal));
            Assert.Equal(sentinel, await File.ReadAllBytesAsync(journal));
            Assert.False(File.Exists(storage.CharacterPath(123)));
            Assert.Equal("Failed", storage.Diagnostic.LastWrite.Status);
            Assert.Equal("None", storage.Diagnostic.RecoveryState);
        }
        finally { RemoveDirectory(directory); }
    }

    private static string NewDirectory() => Path.Combine(AppContext.BaseDirectory, "snapshot-recovery-final", Guid.NewGuid().ToString("N"));

    private static string WriteJournal(string directory, string text)
    {
        var journal = Path.Combine(directory, "state", "write-recovery.json");
        Directory.CreateDirectory(Path.GetDirectoryName(journal)!);
        File.WriteAllText(journal, text);
        return journal;
    }

    private static void RemoveDirectory(string directory)
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
