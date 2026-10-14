using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.Diagnostics;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

[Collection(Exportaramus.Tests.Performance.OfflinePerformanceCollection.Name)]
public sealed class PublicationTelemetryTests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "publication-telemetry", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AcceptedPublicationRecordsEachRequiredPreparationStageInTheInjectedSink()
    {
        var sink = new DiagnosticMeasurements();
        var publisher = new CollectionDataPublisher(new AtomicSnapshotStorage(root, measurements: sink), null, sink);

        var accepted = await publisher.PrepareAndPersistAsync(SnapshotFixture.Create(), "Test", 1, null, false, false, default);

        Assert.Equal(1, accepted.Revision);
        var metrics = sink.Snapshot();
        foreach (var key in new[] { "CompactSerializer", "RichSerializer", "Fingerprints", "FFXIVCollectSerializer", "SnapshotPreparation", "FileWrite" })
        {
            Assert.True(metrics.TryGetValue(key, out var metric), key);
            Assert.True(metric.SampleCount > 0);
        }
        Assert.DoesNotContain(string.Empty, metrics.Keys);
        Assert.False(sink.HasUnrepresentableSamples);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
