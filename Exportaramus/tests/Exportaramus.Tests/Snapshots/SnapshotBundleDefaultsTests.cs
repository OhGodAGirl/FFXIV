using System.Text.Json;
using Exportaramus.Features.CollectionData.Publication;

namespace Exportaramus.Tests.Snapshots;

public sealed class SnapshotBundleDefaultsTests
{
    [Fact]
    public void UnpreparedBundleDoesNotAdvertiseVersionedPayloads()
    {
        var bundle = StateAndPublicationTests.Bundle(SnapshotFixture.Create(), 1);
        Assert.Empty(bundle.CompactJsonV11);
        Assert.Empty(bundle.CompactJsonV12);
        Assert.Empty(bundle.CapabilitiesJsonV12);
        Assert.Empty(bundle.UnavailableReportsCapabilitiesJsonV12);
        Assert.Empty(bundle.CapabilitiesJsonV11);
        Assert.Empty(bundle.UnavailableReportsCapabilitiesJsonV11);
        Assert.Empty(bundle.CapabilitiesJson);
        Assert.Empty(bundle.UnavailableReportsCapabilitiesJson);

        var prepared = SnapshotBundlePreparation.Prepare(bundle, null, true);
        using var compact11 = JsonDocument.Parse(prepared.CompactJsonV11);
        using var compact12 = JsonDocument.Parse(prepared.CompactJsonV12);
        Assert.Equal(JsonValueKind.Object, compact11.RootElement.ValueKind);
        Assert.Equal(JsonValueKind.Object, compact12.RootElement.ValueKind);
        Assert.NotEmpty(prepared.CapabilitiesJsonV12);
        Assert.NotEmpty(prepared.UnavailableReportsCapabilitiesJsonV12);
        Assert.NotEmpty(prepared.CapabilitiesJsonV11);
        Assert.NotEmpty(prepared.UnavailableReportsCapabilitiesJsonV11);
        Assert.NotEmpty(prepared.CapabilitiesJson);
        Assert.NotEmpty(prepared.UnavailableReportsCapabilitiesJson);
        Assert.Empty(bundle.CompactJsonV12);
    }
}
