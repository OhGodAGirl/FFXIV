using Exportaramus.Features.CollectionData;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Tests.Snapshots;

public sealed class BundlePartialMetadataContractTests
{
    [Fact]
    public void CompleteOutfitsDoNotRetainFallbackPartialMetadata()
    {
        var data = OutfitData(completeStorage: true);
        var outfits = data.Collections["outfits"];
        Assert.Equal(2, outfits.Items.Length);
        Assert.Single(outfits.Items.Where(item => item.Owned));
        Assert.Contains(data.State.CurrentItemPresence.Values, source => source.Health.IsUsable && !source.Items.IsEmpty);

        var prepared = SnapshotBundlePreparation.Prepare(StateAndPublicationTests.Bundle(data, 1), null, false);

        Assert.Equal(1, prepared.CollectionOwnedCounts["outfits"]);
        Assert.Empty(prepared.ConfirmedPartialCounts);
    }

    [Fact]
    public void MissingOutfitsRetainConfirmedFallbackWithoutClaimingACompleteTotal()
    {
        var data = OutfitData(completeStorage: false);
        Assert.False(data.Collections.ContainsKey("outfits"));

        var prepared = SnapshotBundlePreparation.Prepare(StateAndPublicationTests.Bundle(data, 1), null, false);

        var partial = Assert.Single(prepared.ConfirmedPartialCounts);
        Assert.Equal("outfits", partial.Key);
        Assert.Equal(1, partial.Value);
        Assert.False(prepared.CollectionOwnedCounts.ContainsKey("outfits"));
        Assert.False(prepared.Data.Collections.ContainsKey("outfits"));
    }

    private static CollectionData OutfitData(bool completeStorage)
    {
        var original = SnapshotFixture.Create();
        var outfits = original.Catalog.Collections["outfits"];
        var unobserved = outfits.Items[0] with { Id = 45095, RequiredItemIds = [999] };
        var catalog = original.Catalog with
        {
            Collections = original.Catalog.Collections.SetItem("outfits", outfits with { Items = [.. outfits.Items, unobserved] }),
            KnownItemIds = original.Catalog.KnownItemIds.Add(999),
        };
        var state = original.State;
        if (!completeStorage)
        {
            state = CollectionStateFactory.Empty(original.State.ContentId, original.State.ProviderSessionId, SnapshotFixture.Now);
            state = state with
            {
                CurrentItemPresence = state.CurrentItemPresence.SetItem("inventory", original.State.CurrentItemPresence["inventory"]),
            };
        }
        return CollectionDataAssembler.Assemble(catalog, state);
    }
}
