using System.Text;
using System.Text.Json;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class EvidenceByteBoundaryTests
{
    private const int Limit = 4194304;

    [Theory]
    [InlineData(10000, true)]
    [InlineData(10001, false)]
    public void UniqueOtherwiseValidReportsHonorTheWholeRecordLimit(int count, bool accepted)
    {
        var json = JsonSerializer.Serialize(new
        {
            contentId = "1",
            items = Enumerable.Range(1, count).Select(id => new { itemId = id, reporter = "Consumer", reportedAtUtc = "2026-09-06T00:00:00Z" })
        });
        Assert.True(Encoding.UTF8.GetByteCount(json) < Limit);
        if (!accepted)
        {
            Assert.Throws<InvalidDataException>(() => CollectionStateReader.ReadReports(json, 1));
            return;
        }
        var reports = CollectionStateReader.ReadReports(json, 1);
        Assert.Equal(count, reports.Length);
        Assert.Equal(1u, reports[0].ItemId);
        Assert.Equal(10000u, reports[^1].ItemId);
    }

    private static string ExactEvidence()
    {
        const string json = "{\"contentId\":\"1\",\"items\":[{\"itemId\":100,\"reporter\":\"é\",\"reportedAtUtc\":\"2026-09-06T00:00:00Z\"}]}";
        return json + new string(' ', Limit - Encoding.UTF8.GetByteCount(json));
    }

    [Fact]
    public void EvidenceReaderAcceptsExactlyFourMibOfValidUtf8JsonAndRejectsOneMoreByte()
    {
        var maximum = ExactEvidence();
        Assert.Equal(Limit, Encoding.UTF8.GetByteCount(maximum)); Assert.True(maximum.Length < Limit);

        var item = Assert.Single(CollectionStateReader.ReadReports(maximum, 1));

        Assert.Equal(100u, item.ItemId); Assert.Equal("é", item.Reporter); Assert.Equal(DateTimeOffset.Parse("2026-09-06T00:00:00Z"), item.ReportedAtUtc);
        var oversized = maximum + " "; Assert.Equal(Limit, oversized.Length);
        Assert.Throws<InvalidDataException>(() => CollectionStateReader.ReadReports(oversized, 1));
    }

    [Fact]
    public async Task EvidenceStorageHonorsTheByteBoundaryAndFailedReplacementPreservesEveryAcceptedOutput()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "isolated-test-recovery/evidence-boundary-proof", Guid.NewGuid().ToString("N"));
        var storage = new AtomicSnapshotStorage(root); var maximum = ExactEvidence();
        try
        {
            await storage.CommitAsync(1, "{\"revision\":1}", "{\"rich\":1}", maximum, true, true, SnapshotFixture.Now, default);
            Assert.Equal(Limit, new FileInfo(storage.EvidencePath(1)).Length);
            Assert.Equal(maximum, await storage.ReadAsync(1, true, default));
            var history = Directory.GetFiles(Path.Combine(root, "snapshots"), "*-snapshot-*.json");
            Assert.Single(history);

            var exception = await Assert.ThrowsAnyAsync<Exception>(() => storage.CommitAsync(1, "{\"revision\":2}", "{\"rich\":2}", maximum + " ", true, true, SnapshotFixture.Now.AddSeconds(1), default));

            Assert.Equal("OutputSizeLimitExceeded", exception.Message);
            Assert.Equal("{\"revision\":1}", await storage.ReadAsync(1, false, default));
            Assert.Equal("{\"rich\":1}", await File.ReadAllTextAsync(storage.CanonicalPath));
            Assert.Equal(maximum, await storage.ReadAsync(1, true, default));
            Assert.Equal(history, Directory.GetFiles(Path.Combine(root, "snapshots"), "*-snapshot-*.json"));
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
            Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
            await File.WriteAllTextAsync(storage.EvidencePath(1), maximum + " ", new UTF8Encoding(false));
            await Assert.ThrowsAsync<InvalidDataException>(() => storage.ReadAsync(1, true, default));
        }
        finally { Directory.Delete(root, true); }
    }
}
