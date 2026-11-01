using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class CompendiumSnapshotWriterTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task WriterPreservesCharacterPayloadsAndRequestedHistoryMode(bool changed, bool manual)
    {
        // Arrange: use distinct payloads so swapped arguments change the files.
        var root = Path.Combine(Path.GetTempPath(), "Exportaramus-writer-" + Guid.NewGuid().ToString("N"));
        var storage = new AtomicSnapshotStorage(root);
        var writer = new CompendiumSnapshotWriter(storage);
        var now = new DateTimeOffset(2026, 10, 31, 12, 34, 56, TimeSpan.Zero);
        const string compact = "{\"compact\":1}";
        const string expanded = "{\"expanded\":2}";
        const string reports = "{\"reports\":3}";
        const string expandedV12 = "{\"expandedV12\":4}";
        try
        {
            // Act.
            var result = await writer.WriteAsync(42, compact, expanded, reports, changed, manual, now, default, expandedV12);

            // Assert: preserve payloads and character IDs. Create history only for requested, changed
            // captures.
            Assert.Equal(compact, await File.ReadAllTextAsync(storage.CharacterPath(42)));
            Assert.Equal(expanded, await File.ReadAllTextAsync(storage.CanonicalPath));
            Assert.Equal(reports, await File.ReadAllTextAsync(storage.EvidencePath(42)));
            Assert.Equal(expandedV12, await File.ReadAllTextAsync(storage.RichV12Path));
            Assert.False(File.Exists(storage.CharacterPath(1)));
            var history = Directory.GetFiles(Path.Combine(root, "snapshots"), "*-snapshot-*.json");
            Assert.Equal(changed ? 1 : 0, result.HistoryCount);
            Assert.Equal(changed ? 1 : 0, history.Length);
            if (changed)
            {
                Assert.Equal($"{(manual ? "manual" : "automatic")}-snapshot-20261031-123456-000.json", Path.GetFileName(history[0]));
                Assert.Equal(expanded, await File.ReadAllTextAsync(history[0]));
                Assert.Equal(now, result.LatestHistoryUtc);
                Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(expanded), result.HistoryBytes);
            }
            else Assert.Null(result.LatestHistoryUtc);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task WriterForwardsCancellationWithoutReplacingAcceptedFiles()
    {
        // Arrange: cancel the replacement before it starts.
        var root = Path.Combine(Path.GetTempPath(), "Exportaramus-writer-" + Guid.NewGuid().ToString("N"));
        var storage = new AtomicSnapshotStorage(root);
        var writer = new CompendiumSnapshotWriter(storage);
        var now = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
        using var cancelled = new CancellationTokenSource();
        try
        {
            await writer.WriteAsync(42, "{\"version\":1}", "{}", null, false, false, now, default);
            cancelled.Cancel();

            // Act.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.WriteAsync(
                42, "{\"version\":2}", "{}", null, false, false, now, cancelled.Token));

            // Assert.
            Assert.Equal("{\"version\":1}", await File.ReadAllTextAsync(storage.CharacterPath(42)));
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
