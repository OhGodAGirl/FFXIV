using System.Text.Json;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class RecoveryTemporaryPathBoundaryTests
{
    [Theory]
    [InlineData(".aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.tmp")]
    [InlineData(".AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.tmp")]
    [InlineData(".aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.tmp.extra")]
    public async Task RecoveryRejectsTemporaryNamesOutsideTheExactOwnedSuffixAndRetainsAllFiles(string suffix)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "recovery-temporary-boundary", Guid.NewGuid().ToString("N"));
        try
        {
            const string destination = "current-snapshot.json";
            var temporary = destination + suffix;
            var backup = temporary + ".bak";
            Directory.CreateDirectory(Path.Combine(root, "state"));
            await File.WriteAllTextAsync(Path.Combine(root, destination), "{\"accepted\":true}");
            await File.WriteAllTextAsync(Path.Combine(root, temporary), "unrelated temporary-shaped file");
            await File.WriteAllTextAsync(Path.Combine(root, backup), "unrelated backup-shaped file");
            var journal = Path.Combine(root, "state", "write-recovery.json");
            await File.WriteAllTextAsync(journal, JsonSerializer.Serialize(new
            {
                format = "exportaramus-write-recovery",
                version = 1,
                committed = true,
                files = new[] { new { destination, temporary, backup, existed = true, delete = false, applied = true } },
            }));
            var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            var storage = new AtomicSnapshotStorage(root);

            await Assert.ThrowsAsync<InvalidDataException>(() => storage.ReadAsync(1, false, default));

            Assert.Equal("Failed", storage.RecoveryState);
            Assert.Equal("Failed", storage.Diagnostic.RecoveryState);
            Assert.Equal("ReadFailed", storage.Diagnostic.LastRead.FailureCode);
            Assert.Equal(before.Keys.Order(), Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order());
            foreach (var pair in before) Assert.Equal(pair.Value, await File.ReadAllBytesAsync(pair.Key));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
