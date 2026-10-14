using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.Inventory;
using Exportaramus.Tests.Snapshots;

namespace Exportaramus.Tests.Main;

public sealed class InventoryOutfitCompletenessTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public void AnOutfitRequiresEveryPieceAcrossObservedLocations(int pieces, int expected)
    {
        var data = SnapshotFixture.Create();
        var definition = data.Catalog.Collections["outfits"] with
        {
            Items = [new(900, 900, "Two-piece outfit", false, RequiredItemIds: [800001, 800002])],
        };
        var presence = data.State.CurrentItemPresence.ToImmutableDictionary(pair => pair.Key,
            pair => pair.Value with { Items = [] });
        if (pieces == 2)
            presence = presence.SetItem("glamourDresser", presence["glamourDresser"] with { Items = [new(800002, null)] });
        data = data with
        {
            Catalog = data.Catalog with { Collections = data.Catalog.Collections.SetItem("outfits", definition) },
            State = data.State with { CurrentItemPresence = presence },
        };
        var bundle = new AcceptedSnapshotBundle(data, 1, "{}", "{}", "", ImmutableDictionary<string, string>.Empty,
            new("{}", [], [], null), 0, null);
        var snapshot = InventoryPolicy.Empty(123, 1);
        if (pieces > 0)
            snapshot = snapshot with
            {
                Locations = [new(new(InventoryLocationKind.Retainer, 1), "Retainer", InventoryObservationState.Retained,
                    SnapshotFixture.Now, SnapshotFixture.Now, [new(800001, 1, 0)])],
            };

        var view = InventoryPresentation.Prepare(snapshot, bundle, SnapshotFixture.Now);

        Assert.Equal(expected, view.ConfirmedOutfits);
    }
}
