using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Tests.Snapshots;
using System.Collections.Immutable;

namespace Exportaramus.Tests.Main;

public sealed class LiveCaptureRegressionTests
{
    [Theory]
    [InlineData(0u, 0u)]
    [InlineData(123u, 123u)]
    [InlineData(1000123u, 123u)]
    [InlineData(2000123u, 123u)]
    public void DresserIdentifiersNormalizeWithoutNativeInventoryObjects(uint encoded, uint expected) =>
        Assert.Equal(expected, DresserSetExpansionPolicy.NormalizeStoredId(encoded));

    [Fact]
    public void AchievementReadinessRequestsOnceAndResetsForAnotherCharacter()
    {
        var edge = new StorageWindowEdgePolicy();
        Assert.False(edge.Observe(false, 0, 0, false));
        Assert.True(edge.Observe(true, 0, 1, false));
        Assert.False(edge.Observe(true, 0, 2, false));
        edge.Reset();
        Assert.True(edge.Observe(true, 0, 3, false));
    }

    [Fact]
    public void ConfirmedOutfitsAreCountedWithoutClaimingMissingStorageIsEmpty()
    {
        var data = SnapshotFixture.Create();
        data = data with { Collections = data.Collections.Remove("outfits") };
        var bundle = new AcceptedSnapshotBundle(data, 1, "{}", "{}", "", ImmutableDictionary<string, string>.Empty,
            new("{}", [], ["outfits"], null), 0, null);
        var prepared = SnapshotBundlePreparation.Prepare(bundle, null, false);
        Assert.Equal(1, prepared.ConfirmedPartialCounts["outfits"]);
        Assert.False(prepared.CollectionOwnedCounts.ContainsKey("outfits"));
        Assert.Contains("outfits", prepared.Export.OmittedKeys);
    }
}
