using System.Collections.Immutable;
using SpringCleaning.Shared.CollectionData;
using SpringCleaning.Shared.Inventory;
using SpringCleaning.Tests.CollectionData;

namespace SpringCleaning.Tests;

public sealed class ProviderActionPlanningTests
{
    [Theory]
    [InlineData(999999u, false)]
    [InlineData(1000000u, true)]
    [InlineData(1000001u, true)]
    public void DestinationQualityClassificationUsesRawIdBoundary(uint rawId, bool quality)
    {
        // Exercise the classifier boundary directly, independently of provider validation.
        var snapshot = Snapshot();
        var entry = snapshot.DresserEntries!.Value.Single(value => value.OutfitId == 900);
        snapshot = snapshot with { DresserEntries = [entry with { RawItemId = rawId }] };
        Assert.Equal(entry.EntryIndex, DresserCompositionPolicy.ExistingOutfit(snapshot, 900, quality));
        Assert.Null(DresserCompositionPolicy.ExistingOutfit(snapshot, 900, !quality));
    }

    [Fact]
    public void SameOutfitIdSelectsOnlyTheMatchingQualityDestination()
    {
        var snapshot = Snapshot();
        var hq = snapshot.DresserEntries!.Value.Single(entry => entry.OutfitId == 900);
        var nq = hq with { EntryIndex = 27, RawItemId = 900 };
        snapshot = snapshot with { DresserEntries = [hq, nq] };
        Assert.Equal(4, DresserCompositionPolicy.ExistingOutfit(snapshot, 900, true));
        Assert.Equal(27, DresserCompositionPolicy.ExistingOutfit(snapshot, 900, false));
        snapshot = snapshot with { DresserEntries = [nq, hq] };
        Assert.Equal(4, DresserCompositionPolicy.ExistingOutfit(snapshot, 900, true));
        Assert.Equal(27, DresserCompositionPolicy.ExistingOutfit(snapshot, 900, false));
        snapshot = snapshot with { DresserEntries = [hq] };
        Assert.Null(DresserCompositionPolicy.ExistingOutfit(snapshot, 900, false));
    }

    [Fact]
    public void MigrationKeepsExactPhysicalEntriesAndOnlyEligibleActualComponents()
    {
        var snapshot = Snapshot();
        var transfers = DresserCompositionPolicy.TransferEntries(snapshot, new HashSet<uint> { 100, 2642, 999 });
        var entry = Assert.Single(transfers);
        Assert.Equal(4, entry.EntryIndex);
        Assert.Equal(1000900u, entry.RawItemId);
        Assert.Equal(900u, entry.OutfitId);
        Assert.Equal(new CollectionDresserComponent(3, 2642), Assert.Single(entry.Components));
        Assert.Equal(3, snapshot.DresserEntries!.Value.Length);
        Assert.Equal(2, snapshot.DresserEntries.Value[1].Components.Length);
    }

    [Fact]
    public void MigrationNeverDiscoversCompositionFromAggregateIds()
    {
        var snapshot = Snapshot();
        Assert.Empty(DresserCompositionPolicy.TransferEntries(null, new HashSet<uint> { 2642 }));
        Assert.Empty(DresserCompositionPolicy.TransferEntries(snapshot with { DresserEntries = null }, new HashSet<uint> { 2642 }));
        Assert.Empty(DresserCompositionPolicy.TransferEntries(snapshot with { GlamourDresser = snapshot.GlamourDresser with { Status = CollectionSourceStatus.Stale } }, new HashSet<uint> { 2642 }));
        Assert.Empty(DresserCompositionPolicy.TransferEntries(snapshot, new HashSet<uint>()));
        Assert.Empty(DresserCompositionPolicy.TransferEntries(snapshot with { Armoire = snapshot.Armoire with { Status = CollectionSourceStatus.Unavailable } }, new HashSet<uint> { 2642 }));
        Assert.Null(DresserCompositionPolicy.ExistingOutfit(null, 900, true));
        Assert.Equal(4, DresserCompositionPolicy.ExistingOutfit(snapshot, 900, true));
        Assert.Null(DresserCompositionPolicy.ExistingOutfit(snapshot, 999, true));
    }

    [Fact]
    public void MissingOnlyStorageRejectsAlreadyRepresentedStandaloneAndPartialOutfitPieces()
    {
        var snapshot = Snapshot();
        snapshot = snapshot with
        {
            Inventory = snapshot.Inventory with { ItemIds = [100, 101, 2642] },
            ArmoryChest = snapshot.ArmoryChest with { ItemIds = [102] },
        };
        Assert.False(DresserCompositionPolicy.CanStore(snapshot, DresserStorageSource.Inventory, 100, false));
        Assert.False(DresserCompositionPolicy.CanStore(snapshot, DresserStorageSource.Inventory, 2642, false));
        Assert.True(DresserCompositionPolicy.CanStore(snapshot, DresserStorageSource.Inventory, 101, false));
        Assert.False(DresserCompositionPolicy.CanStore(snapshot, DresserStorageSource.ArmouryChest, 101, false));
        Assert.True(DresserCompositionPolicy.CanStore(snapshot, DresserStorageSource.ArmouryChest, 102, false));
        Assert.False(DresserCompositionPolicy.CanStore(snapshot with { DresserEntries = null }, DresserStorageSource.Inventory, 101, false));
        Assert.True(DresserCompositionPolicy.CanStore(snapshot, DresserStorageSource.Inventory, 101, true));
    }

    [Fact]
    public void ExistingOutfitDestinationComesFromProviderAndPreservesPartition()
    {
        var snapshot = Snapshot();
        var another = snapshot.DresserEntries!.Value[1] with { EntryIndex = 9, Components = [new(4, 333)] };
        snapshot = snapshot with { DresserEntries = snapshot.DresserEntries.Value.Add(another) };
        Assert.Equal(4, DresserCompositionPolicy.ExistingOutfit(snapshot, 900, true));
        Assert.Equal(new CollectionDresserComponent(4, 333), Assert.Single(snapshot.DresserEntries!.Value[3].Components));
    }

    [Fact]
    public void ExactNativeSelectionRejectsWrongSourceSlotMissingAndExtraPieces()
    {
        var outfit = new InventoryDresserOutfit(900, null, "Set", [new(3202, 7, 3, 10, false, "Piece", 0)]);
        Assert.True(OutfitPieceSelectionPolicy.Matches(outfit, [new(10, 3202, 7)]));
        Assert.False(OutfitPieceSelectionPolicy.Matches(outfit, [new(10, 0, 7)]));
        Assert.False(OutfitPieceSelectionPolicy.Matches(outfit, [new(10, 3202, 8)]));
        Assert.False(OutfitPieceSelectionPolicy.Matches(outfit, [new(11, 3202, 7)]));
        Assert.False(OutfitPieceSelectionPolicy.Matches(outfit, []));
        Assert.False(OutfitPieceSelectionPolicy.Matches(outfit, [new(10, 3202, 7), new(11, 0, 3)]));
        Assert.False(OutfitPieceSelectionPolicy.Matches(outfit with { Components = [] }, []));
        var two = outfit with { Components = [outfit.Components[0], new(3203, 8, 4, 11, false, "Other", 0)] };
        Assert.False(OutfitPieceSelectionPolicy.Matches(two, [new(10, 3202, 7), new(10, 3202, 7)]));
        Assert.True(OutfitPieceSelectionPolicy.Matches(two, [new(11, 3203, 8), new(10, 3202, 7)]));
    }

    [Fact]
    public void LocalCopiesMustAgreeWithSeparateProviderQualityQuantities()
    {
        var observation = ProviderFixture.InventoryObservation();
        Assert.True(InventoryCopyAuthority.Matches(observation, "Inventory", 100, 2, 0));
        Assert.False(InventoryCopyAuthority.Matches(null, "Inventory", 100, 2, 0));
        Assert.False(InventoryCopyAuthority.Matches(observation, "Inventory", 100, null, 0));
        Assert.False(InventoryCopyAuthority.Matches(observation, "Inventory", 100, 2, null));
        Assert.False(InventoryCopyAuthority.Matches(observation, "Retainer", 100, 2, 0));
        Assert.False(InventoryCopyAuthority.Matches(observation, "Inventory", 101, 2, 0));
        Assert.False(InventoryCopyAuthority.Matches(observation, "Inventory", 100, 1, 1));
        Assert.False(InventoryCopyAuthority.Matches(observation, "Inventory", 100, 2, 1));
        Assert.False(InventoryCopyAuthority.Matches(observation with { Locations = observation.Locations.Select(location => location with { State = "Retained" }).ToArray() }, "Inventory", 100, 2, 0));
    }

    [Fact]
    public void StartedDiscardMayUseRetainedPositiveWithinSameProviderScopeOnly()
    {
        var snapshot = Snapshot();
        var candidate = new InventoryDuplicateItem(0, 1, 100, false, "Item", 0, new(true, true, "Ready"), true, true, InventoryDuplicateDisposition.RemovalReview)
        { ProviderContentId = snapshot.ContentId, ProviderSessionId = snapshot.ProviderSessionId };
        Assert.True(CollectionOwnershipPolicy.CanContinueDuplicateRemoval(snapshot, candidate));
        snapshot = snapshot with { GlamourDresser = snapshot.GlamourDresser with { Status = CollectionSourceStatus.Stale } };
        Assert.True(CollectionOwnershipPolicy.CanContinueDuplicateRemoval(snapshot, candidate));
        Assert.True(CollectionOwnershipPolicy.CanPlanInventoryReview(snapshot, candidate.ItemId));
        Assert.False(CollectionOwnershipPolicy.CanContinueDuplicateRemoval(null, candidate));
        Assert.False(CollectionOwnershipPolicy.CanContinueDuplicateRemoval(snapshot with { ContentId = 43 }, candidate));
        Assert.False(CollectionOwnershipPolicy.CanContinueDuplicateRemoval(snapshot with { ProviderSessionId = "new" }, candidate));
        Assert.False(CollectionOwnershipPolicy.CanContinueDuplicateRemoval(snapshot with { Inventory = snapshot.Inventory with { Status = CollectionSourceStatus.Stale } }, candidate));
        Assert.False(CollectionOwnershipPolicy.CanContinueDuplicateRemoval(snapshot with { Inventory = snapshot.Inventory with { ItemIds = [] } }, candidate));
        Assert.False(CollectionOwnershipPolicy.CanContinueDuplicateRemoval(snapshot with { Armoire = snapshot.Armoire with { ItemIds = [] }, GlamourDresser = snapshot.GlamourDresser with { ItemIds = [] } }, candidate));
        Assert.False(CollectionOwnershipPolicy.CanContinueDuplicateRemoval(snapshot with { Armoire = snapshot.Armoire with { Status = CollectionSourceStatus.Unavailable }, GlamourDresser = snapshot.GlamourDresser with { Status = CollectionSourceStatus.Unavailable } }, candidate));
    }

    private static CollectionDataSnapshot Snapshot() => CollectionDataSnapshotParserTests.Parse(ProviderFixture.Snapshot()).Snapshot!;
}
