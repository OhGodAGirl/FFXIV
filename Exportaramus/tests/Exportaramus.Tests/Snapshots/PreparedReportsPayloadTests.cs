using System.Text.Json;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class PreparedReportsPayloadTests
{
    [Fact]
    public void PreparedReportsRetainTheCharacterAndReportedEvidence()
    {
        var data = SnapshotFixture.Create();
        data = CollectionDataAssembler.Assemble(data.Catalog, data.State with
        {
            ReportedEvidence = data.State.ReportedEvidence with
            {
                Items = [new(100, "Fixture", SnapshotFixture.Now)]
            }
        });
        var publisher = new CollectionDataPublisher(new AtomicSnapshotStorage(
            Path.Combine(AppContext.BaseDirectory, "prepared-reports-tests", Guid.NewGuid().ToString("N"))), null);
        Assert.Null(publisher.Prepare(data, "Test", 1, null, false, false, default).Reports);
        var prepared = publisher.Prepare(data, "Test", 1, null, false, true, default);
        using var parsed = JsonDocument.Parse(Assert.IsType<string>(prepared.Reports));
        Assert.Equal("123", parsed.RootElement.GetProperty("contentId").GetString());
        var item = Assert.Single(parsed.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(100u, item.GetProperty("itemId").GetUInt32());
        Assert.Equal("Fixture", item.GetProperty("reporter").GetString());
        Assert.Equal(SnapshotFixture.Now, item.GetProperty("reportedAtUtc").GetDateTimeOffset());
    }
}
