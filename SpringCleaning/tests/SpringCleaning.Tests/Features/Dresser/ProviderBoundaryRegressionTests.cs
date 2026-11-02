using SpringCleaning.Shared.CollectionData;
using SpringCleaning.Shared.Inventory;
using SpringCleaning.Tests.CollectionData;

namespace SpringCleaning.Tests;

public sealed class ProviderBoundaryRegressionTests
{
    [Fact]
    public void RemovingOneItemDoesNotWithdrawOtherCurrentInventoryFacts()
    {
        var root = ProviderFixture.Snapshot();
        ProviderFixture.Items(root, "inventory", [200, 201]);
        var inventory = ProviderFixture.InventoryObservation(root);
        inventory = inventory with
        {
            Locations = inventory.Locations.Select(location => location.Kind == "Inventory"
                ? location with { Items = [new(201, 1, 0)] } : location).ToArray(),
        };

        var parsed = CollectionDataSnapshotParserTests.Parse(root, inventory: inventory);

        Assert.Equal(CollectionSourceStatus.Fresh, parsed.Snapshot!.Inventory.Status);
        Assert.Equal(CollectionOwnershipFact.Owned, CollectionOwnershipPolicy.GetFact(parsed.Snapshot, CollectionSourceName.Inventory, 201));
        Assert.Equal(CollectionOwnershipFact.Missing, CollectionOwnershipPolicy.GetFact(parsed.Snapshot, CollectionSourceName.Inventory, 200));
        Assert.False(InventoryCopyAuthority.Matches(inventory, "Inventory", 200, 1, 0));
        Assert.True(InventoryCopyAuthority.Matches(inventory, "Inventory", 201, 1, 0));
    }

    [Fact]
    public void BroaderInventoryFeedWithCrystalsStillCoversCanonicalCarriedItems()
    {
        var root = ProviderFixture.Snapshot();
        ProviderFixture.Items(root, "inventory", [200]);
        var inventory = ProviderFixture.InventoryObservation(root);
        inventory = inventory with
        {
            Locations = inventory.Locations.Select(location => location.Kind == "Inventory"
                ? location with { Items = [new(2, 99, 0), new(200, 1, 0)] } : location).ToArray(),
        };

        var parsed = CollectionDataSnapshotParserTests.Parse(root, inventory: inventory);

        Assert.Equal(CollectionSourceStatus.Fresh, parsed.Snapshot!.Inventory.Status);
        Assert.Equal(new uint[] { 200 }, parsed.Snapshot.Inventory.ItemIds);
        Assert.False(InventoryCopyAuthority.Matches(inventory, "Inventory", 200, 2, 0));
        Assert.Empty(InventoryPresenceAuthority.CurrentIds(inventory, "inventory", [201]));
    }

    [Fact]
    public void EquippedPresenceReconcilesWithArmouryWithoutAuthorizingAnEquippedLocator()
    {
        var root = ProviderFixture.Snapshot();
        ProviderFixture.Items(root, "armoryChest", [200, 201]);
        var inventory = ProviderFixture.InventoryObservation(root);
        inventory = inventory with
        {
            Locations = inventory.Locations.Select(location => location.Kind switch
            {
                "ArmoryChest" => location with { Items = [new(200, 1, 0)] },
                "Equipped" => location with { Items = [new(201, 1, 0), new(200, 1, 0)] },
                _ => location,
            }).ToArray(),
        };

        var parsed = CollectionDataSnapshotParserTests.Parse(root, inventory: inventory);
        Assert.True(parsed.Accepted);
        Assert.Equal(CollectionSourceStatus.Fresh, parsed.Snapshot!.ArmoryChest.Status);
        Assert.Equal(new uint[] { 200, 201 }, parsed.Snapshot.ArmoryChest.ItemIds);
        Assert.True(InventoryCopyAuthority.Matches(inventory, "ArmoryChest", 200, 1, 0));
        Assert.False(InventoryCopyAuthority.Matches(inventory, "ArmoryChest", 201, 1, 0));
        Assert.True(InventoryPresenceAuthority.IsReady(inventory, "42", "armoryChest"));
        Assert.Equal(new uint[] { 200 }, InventoryPresenceAuthority.CurrentIds(inventory, "armoryChest", [200]));
        Assert.Equal(new uint[] { 200 }, InventoryPresenceAuthority.CurrentIds(inventory, "armoryChest", [200, 202]));
        foreach (var invalid in new[]
        {
            inventory with { Locations = inventory.Locations.Where(location => location.Kind != "Equipped").ToArray() },
            inventory with { Locations = inventory.Locations.Select(location => location.Kind == "Equipped" ? location with { State = "Retained" } : location).ToArray() },
        })
            Assert.Equal(CollectionSourceStatus.Unavailable, CollectionDataSnapshotParserTests.Parse(root, inventory: invalid).Snapshot!.ArmoryChest.Status);
    }

    [Fact]
    public void ArmoireOnlyReviewOffersRemovalAndNeverDuplicatesDresserStorage()
    {
        var snapshot = CollectionDataSnapshotParserTests.Parse(ProviderFixture.Snapshot()).Snapshot!;
        snapshot = snapshot with { GlamourDresser = snapshot.GlamourDresser with { ItemIds = [] } };
        var candidate = new InventoryDuplicateItem(0, 2, 100, false, "Item", 0, new(true, true, "Ready"), true, false,
            InventoryDuplicatePlanner.GetDisposition(true, false))
        { ProviderContentId = snapshot.ContentId, ProviderSessionId = snapshot.ProviderSessionId };

        Assert.True(CollectionOwnershipPolicy.CanPlanInventoryReview(snapshot, 100));
        Assert.Equal(InventoryDuplicateDisposition.RemovalReview, candidate.Disposition);
        Assert.Single(InventoryDuplicatePlanner.SelectBulkDiscardCandidates([candidate]));
        Assert.True(CollectionOwnershipPolicy.CanContinueDuplicateRemoval(snapshot, candidate));
        Assert.False(CollectionOwnershipPolicy.CanOpenDresserFallback(snapshot, candidate));
        Assert.False(CollectionOwnershipPolicy.CanOpenDresserFallback(null, candidate));
        Assert.False(CollectionOwnershipPolicy.CanOpenDresserFallback(snapshot with { ProviderSessionId = "restarted" }, candidate));
        Assert.False(CollectionOwnershipPolicy.CanOpenDresserFallback(snapshot with { GlamourDresser = snapshot.GlamourDresser with { Status = CollectionSourceStatus.Stale } }, candidate));
        Assert.False(CollectionOwnershipPolicy.CanOpenDresserFallback(snapshot with { GlamourDresser = snapshot.GlamourDresser with { ItemIds = [100] } }, candidate));
        Assert.False(CollectionOwnershipPolicy.CanOpenDresserFallback(snapshot, candidate with { Disposition = InventoryDuplicateDisposition.RemovalReview }));
    }

    [Fact]
    public void EmptyAnchorNameDoesNotManufactureWindowFocus()
    {
        Assert.False(NativeWindowFocusPolicy.IsAnchorFocused(1, string.Empty, 2, 2, string.Empty, 2, 2));
    }
}
