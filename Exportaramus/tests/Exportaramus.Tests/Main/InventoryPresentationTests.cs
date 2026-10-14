using System.Collections.Immutable;
using Exportaramus.Features.Inventory;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Tests.Snapshots;

namespace Exportaramus.Tests.Main;

public sealed class InventoryPresentationTests
{
    [Fact]
    public void ObservedCollectionStorageCanConfirmAnOutfitWithoutInventoryCopies()
    {
        var data = SnapshotFixture.Create();
        var outfits = data.Catalog.Collections["outfits"] with { Items = [new(900, 900, "Outfit", false, RequiredItemIds: [100]), new(901, 901, "Missing", false, RequiredItemIds: [101]), new(902, 902, "Unmapped", false)] };
        data = data with
        {
            Catalog = data.Catalog with { Collections = data.Catalog.Collections.SetItem("outfits", outfits) },
            State = data.State with
            {
                CurrentItemPresence = data.State.CurrentItemPresence.SetItem("glamourDresser",
                new([new(100, null)], false, SnapshotFixture.Health(Exportaramus.Features.CollectionData.State.DataType.CurrentItemPresence)))
            }
        };
        var bundle = new AcceptedSnapshotBundle(data, 1, "{}", "{}", "", ImmutableDictionary<string, string>.Empty,
            new("{}", [], [], null), 0, null);
        Assert.Equal(1, InventoryPresentation.Prepare(InventoryPolicy.Empty(123, 1), bundle, DateTimeOffset.UtcNow).ConfirmedOutfits);
    }

    [Fact]
    public void UnavailableCollectionStorageDoesNotConfirmOutfits()
    {
        var data = SnapshotFixture.Create();
        data = data with
        {
            State = data.State with
            {
                CurrentItemPresence = data.State.CurrentItemPresence.ToImmutableDictionary(pair => pair.Key,
            pair => pair.Value with { Health = pair.Value.Health with { SourceStatus = Exportaramus.Features.CollectionData.State.SourceStatus.Unavailable } })
            }
        };
        var bundle = new AcceptedSnapshotBundle(data, 1, "{}", "{}", "", ImmutableDictionary<string, string>.Empty,
            new("{}", [], ["outfits"], null), 0, null);
        var snapshot = InventoryPolicy.Empty(123, 1);
        Assert.Equal(0, InventoryPresentation.Prepare(snapshot, bundle, DateTimeOffset.UtcNow).ConfirmedOutfits);
        bundle = bundle with { Data = data with { Catalog = data.Catalog with { Collections = data.Catalog.Collections.Remove("outfits") } } };
        Assert.Null(InventoryPresentation.Prepare(snapshot, bundle, DateTimeOffset.UtcNow).ConfirmedOutfits);
    }

    [Fact]
    public void OneCapturedRetainerContributesBeforeAllRetainersAreRead()
    {
        var data = SnapshotFixture.Create();
        var outfit = data.Catalog.Collections["outfits"];
        outfit = outfit with { Items = [new(900, 900, "Outfit", false, RequiredItemIds: [800001u, 800002u])] };
        data = data with { Catalog = data.Catalog with { Collections = data.Catalog.Collections.SetItem("outfits", outfit) } };
        var bundle = new AcceptedSnapshotBundle(data, 1, "{}", "{}", "", ImmutableDictionary<string, string>.Empty,
            new("{}", [], ["outfits"], null), 0, null);
        var now = DateTimeOffset.UtcNow;
        InventorySnapshot snapshot = new(123, 1, true,
            [new(new(InventoryLocationKind.Retainer, 1), "Alice", InventoryObservationState.Retained, now, now, [new(800001, 1, 0),new(800002,0,1)]),
             new(new(InventoryLocationKind.Retainer, 2), "Bob", InventoryObservationState.Unknown, null, null, [])]);
        var view = InventoryPresentation.Prepare(snapshot, bundle, now);
        Assert.Equal(1, view.ConfirmedOutfits);
        Assert.Equal("Retainers: 1/2 captured", view.Summary);
        Assert.Contains(view.Locations, row => row.Contains("Bob: Not yet captured"));
    }
}
