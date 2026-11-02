using System.Collections.Immutable;
using SpringCleaning.Shared.CollectionData;

namespace SpringCleaning.Tests;

public sealed class CollectionOwnershipPolicyTests
{
    [Fact]
    public void SnapshotSourceLookupCoversEveryCanonicalSourceAndRejectsUnknownValues()
    {
        var snapshot = Snapshot();

        Assert.Same(snapshot.Inventory, snapshot.GetSource(CollectionSourceName.Inventory));
        Assert.Same(snapshot.ArmoryChest, snapshot.GetSource(CollectionSourceName.ArmoryChest));
        Assert.Same(snapshot.ChocoboSaddlebag, snapshot.GetSource(CollectionSourceName.ChocoboSaddlebag));
        Assert.Same(snapshot.PremiumSaddlebag, snapshot.GetSource(CollectionSourceName.PremiumSaddlebag));
        Assert.Same(snapshot.Retainers, snapshot.GetSource(CollectionSourceName.Retainers));
        Assert.Same(snapshot.Armoire, snapshot.GetSource(CollectionSourceName.Armoire));
        Assert.Same(snapshot.GlamourDresser, snapshot.GetSource(CollectionSourceName.GlamourDresser));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.GetSource((CollectionSourceName)999));
    }

    [Fact]
    public void FreshSourceGateRequiresSnapshotSourcesAndAtLeastOneRequirement()
    {
        var snapshot = Snapshot(
            inventory: Source(CollectionSourceStatus.Fresh, []),
            dresser: Source(CollectionSourceStatus.Stale, []));

        Assert.True(CollectionOwnershipPolicy.AreSourcesFresh(
            snapshot,
            CollectionSourceName.Inventory));
        Assert.False(CollectionOwnershipPolicy.AreSourcesFresh(
            snapshot,
            CollectionSourceName.Inventory,
            CollectionSourceName.GlamourDresser));
        Assert.False(CollectionOwnershipPolicy.AreSourcesFresh(snapshot));
        Assert.False(CollectionOwnershipPolicy.AreSourcesFresh(
            null,
            CollectionSourceName.Inventory));
    }

    [Fact]
    public void DresserSourceReadinessDoesNotDependOnTheOtherSourceOrArmoire()
    {
        var snapshot = Snapshot(
            inventory: Source(CollectionSourceStatus.Fresh, [17u]),
            armoryChest: Source(CollectionSourceStatus.Stale, [18u]),
            armoire: Source(CollectionSourceStatus.Unavailable, []),
            dresser: Source(CollectionSourceStatus.Fresh, []));

        Assert.True(CollectionOwnershipPolicy.AreSourcesFresh(
            snapshot,
            CollectionSourceName.Inventory,
            CollectionSourceName.GlamourDresser));
        Assert.False(CollectionOwnershipPolicy.AreSourcesFresh(
            snapshot,
            CollectionSourceName.ArmoryChest,
            CollectionSourceName.GlamourDresser));
    }

    [Theory]
    [InlineData((int)CollectionSourceStatus.Fresh, true, (int)CollectionOwnershipFact.Owned)]
    [InlineData((int)CollectionSourceStatus.Fresh, false, (int)CollectionOwnershipFact.Missing)]
    [InlineData((int)CollectionSourceStatus.Stale, true, (int)CollectionOwnershipFact.Owned)]
    [InlineData((int)CollectionSourceStatus.Stale, false, (int)CollectionOwnershipFact.Unknown)]
    [InlineData((int)CollectionSourceStatus.Unavailable, true, (int)CollectionOwnershipFact.Unknown)]
    [InlineData((int)CollectionSourceStatus.Unavailable, false, (int)CollectionOwnershipFact.Unknown)]
    [InlineData((int)CollectionSourceStatus.NeverObserved, true, (int)CollectionOwnershipFact.Unknown)]
    [InlineData((int)CollectionSourceStatus.NeverObserved, false, (int)CollectionOwnershipFact.Unknown)]
    public void OwnershipFactsPreserveUnknownAndStaleProtection(
        int statusValue,
        bool containsItem,
        int expectedValue)
    {
        var status = (CollectionSourceStatus)statusValue;
        var expected = (CollectionOwnershipFact)expectedValue;
        var snapshot = Snapshot(inventory: Source(status, containsItem ? [17u] : []));

        var fact = CollectionOwnershipPolicy.GetFact(
            snapshot,
            CollectionSourceName.Inventory,
            17);

        Assert.Equal(expected, fact);
    }

    [Fact]
    public void MissingSnapshotAndInvalidItemRemainUnknown()
    {
        Assert.Equal(
            CollectionOwnershipFact.Unknown,
            CollectionOwnershipPolicy.GetFact(null, CollectionSourceName.Inventory, 17));
        Assert.Equal(
            CollectionOwnershipFact.Unknown,
            CollectionOwnershipPolicy.GetFact(Snapshot(), CollectionSourceName.Inventory, 0));
    }

    [Theory]
    [InlineData((int)CollectionSourceStatus.Fresh, (int)CollectionSourceStatus.Fresh, true)]
    [InlineData((int)CollectionSourceStatus.Stale, (int)CollectionSourceStatus.Fresh, false)]
    [InlineData((int)CollectionSourceStatus.Fresh, (int)CollectionSourceStatus.Stale, false)]
    [InlineData((int)CollectionSourceStatus.Fresh, (int)CollectionSourceStatus.Unavailable, false)]
    public void TransferPlanningRequiresFreshOwnedSourceAndFreshMissingDestination(
        int sourceStatusValue,
        int destinationStatusValue,
        bool expected)
    {
        var sourceStatus = (CollectionSourceStatus)sourceStatusValue;
        var destinationStatus = (CollectionSourceStatus)destinationStatusValue;
        var snapshot = Snapshot(
            inventory: Source(sourceStatus, [17u]),
            dresser: Source(destinationStatus, []));

        var canPlan = CollectionOwnershipPolicy.CanPlanTransfer(
            snapshot,
            CollectionSourceName.Inventory,
            CollectionSourceName.GlamourDresser,
            17);

        Assert.Equal(expected, canPlan);
    }

    [Theory]
    [InlineData(false, (int)CollectionSourceStatus.Unavailable, true, 0u, true)]
    [InlineData(true, (int)CollectionSourceStatus.Fresh, true, 0u, true)]
    [InlineData(true, (int)CollectionSourceStatus.Fresh, false, 17u, true)]
    [InlineData(true, (int)CollectionSourceStatus.Stale, false, 0u, true)]
    [InlineData(true, (int)CollectionSourceStatus.Fresh, false, 0u, false)]
    public void DresserPlanningRequiresArmoireAbsenceOnlyForEligibleItems(
        bool armoireEligible,
        int armoireStatusValue,
        bool expected,
        uint storedArmoireItem,
        bool sourceContainsItem)
    {
        var armoireStatus = (CollectionSourceStatus)armoireStatusValue;
        var snapshot = Snapshot(
            inventory: Source(CollectionSourceStatus.Fresh, sourceContainsItem ? [17u] : []),
            armoire: Source(armoireStatus, storedArmoireItem == 0 ? [] : [storedArmoireItem]),
            dresser: Source(CollectionSourceStatus.Fresh, []));

        var canPlan = CollectionOwnershipPolicy.CanPlanDresserStorage(
            snapshot,
            CollectionSourceName.Inventory,
            17,
            armoireEligible);

        Assert.Equal(expected, canPlan);
    }

    [Theory]
    [InlineData((int)CollectionSourceStatus.Fresh, (int)CollectionSourceStatus.Fresh, true)]
    [InlineData((int)CollectionSourceStatus.Stale, (int)CollectionSourceStatus.Fresh, false)]
    [InlineData((int)CollectionSourceStatus.Fresh, (int)CollectionSourceStatus.Stale, true)]
    [InlineData((int)CollectionSourceStatus.Fresh, (int)CollectionSourceStatus.Unavailable, false)]
    public void DuplicateRemovalRequiresFreshInventoryAndPositiveStoredCopy(
        int inventoryStatusValue,
        int dresserStatusValue,
        bool expected)
    {
        var inventoryStatus = (CollectionSourceStatus)inventoryStatusValue;
        var dresserStatus = (CollectionSourceStatus)dresserStatusValue;
        var snapshot = Snapshot(
            inventory: Source(inventoryStatus, [17u]),
            dresser: Source(dresserStatus, [17u]));

        Assert.Equal(expected, CollectionOwnershipPolicy.CanPlanInventoryReview(snapshot, 17));
    }

    [Fact]
    public void DuplicateRemovalAcceptsAFreshArmoireCopyAndRejectsNoSnapshot()
    {
        var snapshot = Snapshot(
            inventory: Source(CollectionSourceStatus.Fresh, [17u]),
            armoire: Source(CollectionSourceStatus.Fresh, [17u]),
            dresser: Source(CollectionSourceStatus.Fresh, []));

        Assert.True(CollectionOwnershipPolicy.CanPlanInventoryReview(snapshot, 17));
        Assert.False(CollectionOwnershipPolicy.CanPlanInventoryReview(null, 17));
    }

    [Fact]
    public void PartialExistingNewWorldAndSurvivalOutfitsPlanEveryMissingPiece()
    {
        uint[] missingPieces = [15464, 15465, 15466, 15472, 15473];
        var snapshot = Snapshot(
            inventory: Source(CollectionSourceStatus.Fresh, missingPieces),
            armoire: Source(CollectionSourceStatus.Fresh, []),
            dresser: Source(CollectionSourceStatus.Fresh, [15463, 15474, 15475]));

        Assert.All(missingPieces, itemId => Assert.True(
            CollectionOwnershipPolicy.CanPlanDresserStorage(
                snapshot,
                CollectionSourceName.Inventory,
                itemId,
                isArmoireEligible: false)));
        Assert.Equal(
            CollectionOwnershipFact.Owned,
            CollectionOwnershipPolicy.GetFact(
                snapshot,
                CollectionSourceName.GlamourDresser,
                15463));
        Assert.Equal(
            CollectionOwnershipFact.Owned,
            CollectionOwnershipPolicy.GetFact(
                snapshot,
                CollectionSourceName.GlamourDresser,
                15474));
    }

    [Fact]
    public void SeededOwnershipFactsNeverInferMissingFromNonFreshSources()
    {
        var random = new Random(0xC011EC7);
        for (var iteration = 0; iteration < 1_000; iteration++)
        {
            var status = (CollectionSourceStatus)random.Next(0, 4);
            var contains = random.Next(0, 2) == 0;
            var snapshot = Snapshot(
                inventory: Source(status, contains ? [17u] : []));

            var actual = CollectionOwnershipPolicy.GetFact(
                snapshot,
                CollectionSourceName.Inventory,
                17);
            var expected = status switch
            {
                CollectionSourceStatus.Fresh when contains => CollectionOwnershipFact.Owned,
                CollectionSourceStatus.Fresh => CollectionOwnershipFact.Missing,
                CollectionSourceStatus.Stale when contains => CollectionOwnershipFact.Owned,
                _ => CollectionOwnershipFact.Unknown,
            };

            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void ReadinessIdentifiesTheUnavailableSourceAndProviderReason()
    {
        var snapshot = Snapshot(dresser: Source(CollectionSourceStatus.Stale, [17u]) with
        { Reason = "Open the Glamour Dresser." });

        Assert.Equal("Exportaramus: Glamour Dresser is Stale. Open the Glamour Dresser.",
            CollectionOwnershipPolicy.ReadinessMessage(snapshot, CollectionSourceName.Inventory, CollectionSourceName.GlamourDresser));
        Assert.False(CollectionOwnershipPolicy.CanPlanTransfer(snapshot, CollectionSourceName.Inventory, CollectionSourceName.GlamourDresser, 18));
    }

    [Fact]
    public void ReadinessDistinguishesNoSnapshotFromMissingEntryDetailsAndReadyEmptyEntries()
    {
        Assert.Equal("Waiting for an Exportaramus snapshot for this character.",
            CollectionOwnershipPolicy.ReadinessMessage(null, CollectionSourceName.Inventory));
        Assert.Equal("Exportaramus has no Glamour Dresser entry details for this snapshot.",
            CollectionOwnershipPolicy.ReadinessMessage(Snapshot(), CollectionSourceName.GlamourDresser));
        Assert.Equal("Exportaramus ownership data is ready.",
            CollectionOwnershipPolicy.ReadinessMessage(Snapshot() with { DresserEntries = [] }, CollectionSourceName.GlamourDresser));
        Assert.Equal("Exportaramus: Armoury Chest is Unavailable.",
            CollectionOwnershipPolicy.ReadinessMessage(Snapshot(armoryChest: Source(CollectionSourceStatus.Unavailable, [])), CollectionSourceName.ArmoryChest));
    }

    [Fact]
    public void RetainedDresserPositivePermitsReviewWithoutArmoireButRetainedAbsenceDoesNot()
    {
        var snapshot = Snapshot(inventory: Source(CollectionSourceStatus.Fresh, [17u]),
            dresser: Source(CollectionSourceStatus.Stale, [17u]),
            armoire: Source(CollectionSourceStatus.Unavailable, []));

        Assert.True(CollectionOwnershipPolicy.CanPlanInventoryReview(snapshot, 17));
        Assert.True(CollectionOwnershipPolicy.CanPlanInventoryReview(snapshot with
        {
            GlamourDresser = Source(CollectionSourceStatus.Stale, []),
            Armoire = Source(CollectionSourceStatus.Fresh, [17u]),
        }, 17));
    }

    [Fact]
    public void ArmouryDuplicatesRequireTheirOwnFreshSourceAndRetainedAppearance()
    {
        var snapshot = Snapshot(inventory: Source(CollectionSourceStatus.Fresh, []),
            armoryChest: Source(CollectionSourceStatus.Fresh, [17u]),
            dresser: Source(CollectionSourceStatus.Fresh, []),
            armoire: Source(CollectionSourceStatus.Fresh, [17u]));
        Assert.True(CollectionOwnershipPolicy.CanPlanInventoryReview(snapshot, 17, CollectionSourceName.ArmoryChest));
        Assert.False(CollectionOwnershipPolicy.CanPlanInventoryReview(snapshot, 17));
        Assert.False(CollectionOwnershipPolicy.CanPlanInventoryReview(snapshot with
        { ArmoryChest = Source(CollectionSourceStatus.Stale, [17u]) }, 17, CollectionSourceName.ArmoryChest));
        Assert.False(CollectionOwnershipPolicy.CanPlanInventoryReview(snapshot with
        { Armoire = Source(CollectionSourceStatus.Fresh, []) }, 17, CollectionSourceName.ArmoryChest));
        Assert.False(CollectionOwnershipPolicy.CanPlanInventoryReview(snapshot, 17, CollectionSourceName.Armoire));
        var candidate = new InventoryDuplicateItem(3200, 0, 17, false, "Item", 0,
            new(true, true, "Ready"), true, false, InventoryDuplicateDisposition.RemovalReview)
        {
            Source = CollectionSourceName.ArmoryChest,
            ProviderContentId = snapshot.ContentId,
            ProviderSessionId = snapshot.ProviderSessionId
        };
        Assert.True(CollectionOwnershipPolicy.CanContinueDuplicateRemoval(snapshot, candidate));
        Assert.False(CollectionOwnershipPolicy.CanContinueDuplicateRemoval(snapshot, candidate with
        { Source = CollectionSourceName.Inventory }));
    }

    [Fact]
    public void DiscardCandidateCannotInvokeLegacyStoreFallback()
    {
        var snapshot = Snapshot();
        var candidate = new InventoryDuplicateItem(0, 0, 17, false, "Item", 0,
            new(true, true, "Ready"), true, false, InventoryDuplicateDisposition.RemovalReview)
        { ProviderContentId = snapshot.ContentId, ProviderSessionId = snapshot.ProviderSessionId };
        Assert.False(CollectionOwnershipPolicy.CanOpenDresserFallback(snapshot, candidate));
    }

    [Fact]
    public void ExplicitLegacyFallbackStillRequiresFreshTransferProof()
    {
        var snapshot = Snapshot(inventory: Source(CollectionSourceStatus.Fresh, [17u]));
        var candidate = new InventoryDuplicateItem(0, 0, 17, false, "Item", 0,
            new(true, true, "Ready"), true, false, InventoryDuplicateDisposition.DresserFallback)
        { ProviderContentId = snapshot.ContentId, ProviderSessionId = snapshot.ProviderSessionId };
        Assert.True(CollectionOwnershipPolicy.CanOpenDresserFallback(snapshot, candidate));
        Assert.False(CollectionOwnershipPolicy.CanOpenDresserFallback(snapshot with
        { GlamourDresser = Source(CollectionSourceStatus.Stale, []) }, candidate));
        Assert.False(CollectionOwnershipPolicy.CanOpenDresserFallback(snapshot with
        { GlamourDresser = Source(CollectionSourceStatus.Fresh, [17u]) }, candidate));
    }
    private static CollectionDataSnapshot Snapshot(
        CollectionSourceSnapshot? inventory = null,
        CollectionSourceSnapshot? armoryChest = null,
        CollectionSourceSnapshot? armoire = null,
        CollectionSourceSnapshot? dresser = null)
    {
        var empty = Source(CollectionSourceStatus.Fresh, []);
        return new CollectionDataSnapshot(
            "CollectionLedgerHelper",
            "1.0.0.0",
            42,
            1,
            new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero),
            inventory ?? empty,
            armoryChest ?? empty,
            empty,
            empty,
            empty,
            armoire ?? empty,
            dresser ?? empty);
    }

    private static CollectionSourceSnapshot Source(
        CollectionSourceStatus status,
        uint[] itemIds)
    {
        var observed = status is CollectionSourceStatus.Fresh or CollectionSourceStatus.Stale
            ? new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero)
            : (DateTimeOffset?)null;
        return new CollectionSourceSnapshot(
            status,
            observed,
            itemIds.Order().ToImmutableArray(),
            null);
    }
}
