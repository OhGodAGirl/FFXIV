using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.Inventory;
using Exportaramus.Tests.Snapshots;

namespace Exportaramus.Tests.Main;

public sealed class InventoryPresentationIdentityTests
{
    [Fact]
    public void OutfitLowerBoundDoesNotCombineDifferentCharacters()
    {
        // Arrange: give each character a different outfit piece.
        var data = SnapshotFixture.Create();
        var definition = data.Catalog.Collections["outfits"] with
        {
            Items = [new(900, 900, "Two-piece outfit", false, RequiredItemIds: [800001, 800002])],
        };
        data = data with
        {
            Catalog = data.Catalog with
            {
                Collections = data.Catalog.Collections.SetItem("outfits", definition)
            },
            State = data.State with
            {
                CurrentItemPresence = data.State.CurrentItemPresence.ToImmutableDictionary(pair => pair.Key,
                    pair => pair.Value with { Items = pair.Key == "glamourDresser" ? [new(800001, null)] : [] }),
            },
        };
        var bundle = new AcceptedSnapshotBundle(data, 1, "{}", "{}", "", ImmutableDictionary<string, string>.Empty,
            new("{}", [], [], null), 0, null);
        var snapshot = InventoryPolicy.Empty(456, 1) with
        {
            Locations = [new(new(InventoryLocationKind.Inventory), "Inventory", InventoryObservationState.Observed,
                SnapshotFixture.Now, SnapshotFixture.Now, [new(800002, 1, 0)])],
        };

        // Act.
        var view = InventoryPresentation.Prepare(snapshot, bundle, SnapshotFixture.Now);

        // Assert: do not combine pieces from different characters.
        Assert.Null(view.ConfirmedOutfits);
        Assert.Single(view.Locations);
        Assert.Equal(1, InventoryPresentation.Prepare(snapshot with
        {
            ContentId = 123
        }, bundle, SnapshotFixture.Now).ConfirmedOutfits);
    }
}
