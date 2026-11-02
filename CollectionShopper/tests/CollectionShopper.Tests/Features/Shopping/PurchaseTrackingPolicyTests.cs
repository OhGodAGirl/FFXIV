namespace CollectionShopper.Tests;

public sealed class PurchaseTrackingPolicyTests
{
    private const ulong Character = ExportaramusFixture.ContentId;
    private static readonly DateTimeOffset Now = ExportaramusFixture.Now;
    private static readonly ShoppingItem Item = new(100_000, "Item", 0, "Collection", null, "", "", 0,
        true, false, null, ShoppingCategory.Outfit, 0, 1);

    [Fact]
    public void PreparationSeparatesCharacterPendingAcknowledgedAndLegacyRecords()
    {
        // Arrange
        List<PendingPurchase> records = [new(Character, 1, Now), new(Character, 2, Now, Now), new(42, 3, Now)];
        HashSet<uint> archive = [1, 2, 3, 4];

        // Act
        var current = PurchaseTrackingPolicy.Prepare(records, archive, Character, Now);
        var other = PurchaseTrackingPolicy.Prepare(records, archive, 42, Now);
        var loggedOut = PurchaseTrackingPolicy.Prepare(records, archive, 0, Now);

        // Assert
        Assert.True(current.IsValid);
        Assert.Equal(Character, current.ContentId);
        Assert.Equal(new uint[] { 1 }, current.PendingItemIds);
        Assert.Equal(Now, Assert.Single(current.AcknowledgedAcquisitions).Value);
        Assert.Equal(2u, Assert.Single(current.AcknowledgedAcquisitions).Key);
        Assert.True(current.LegacyPurchaseArchive.SetEquals([3, 4]));
        Assert.Equal(new uint[] { 3 }, other.PendingItemIds);
        Assert.Empty(other.AcknowledgedAcquisitions);
        Assert.True(other.LegacyPurchaseArchive.SetEquals([1, 2, 4]));
        Assert.Empty(loggedOut.PendingItemIds);
        Assert.Empty(loggedOut.AcknowledgedAcquisitions);
        Assert.True(loggedOut.LegacyPurchaseArchive.SetEquals(archive));
        records.Clear();
        archive.Clear();
        Assert.Equal(new uint[] { 1 }, current.PendingItemIds);
        Assert.True(current.LegacyPurchaseArchive.SetEquals([3, 4]));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidRecordsNeverPublishSuppressionOrModifySavedEvidence(bool absent)
    {
        // Arrange
        List<PendingPurchase>? records = absent ? null : [new(Character, 0, Now)];
        HashSet<uint> archive = [1];

        // Act
        var tracking = PurchaseTrackingPolicy.Prepare(records, archive, Character, Now);

        // Assert
        Assert.False(tracking.IsValid);
        Assert.Equal(Character, tracking.ContentId);
        Assert.Empty(tracking.PendingItemIds);
        Assert.Empty(tracking.AcknowledgedAcquisitions);
        Assert.Empty(tracking.LegacyPurchaseArchive);
        Assert.Equal(new uint[] { 1 }, archive);
        if (!absent) Assert.Equal(new PendingPurchase(Character, 0, Now), Assert.Single(records!));
    }

    [Fact]
    public void ReconciliationAcknowledgesOnlyCurrentUnacknowledgedObservedItems()
    {
        // Arrange
        List<PendingPurchase> records = [new(Character, 100_000, Now), new(42, 100_000, Now),
            new(Character, 100_001, Now, Now), new(Character, 100_002, Now), new(Character, 100_003, Now)];
        var before = records.ToArray();
        var tracking = Prepare(records);
        var snapshot = ExportaramusFixture.Snapshot();
        snapshot = snapshot with {
            CurrentItemPresence = snapshot.CurrentItemPresence.SetItem("inventory", new([new(100_000, 1), new(100_002, 1)])),
            SourceHealth = snapshot.SourceHealth.SetItem("inventory", snapshot.SourceHealth["inventory"] with {
                ObservedAtUtc = Now.AddSeconds(1).ToString("O") }),
        };
        var ownership = ExportaramusFixture.State(snapshot);

        // Act
        var changed = PurchaseTrackingPolicy.Reconcile(tracking, records, Character, ownership);
        var repeated = PurchaseTrackingPolicy.Reconcile(tracking, records, Character, ownership);

        // Assert
        Assert.True(changed);
        Assert.False(repeated);
        Assert.Equal(before[0] with { AcknowledgedAtUtc = Now.AddSeconds(1) }, records[0]);
        Assert.Equal(before[1], records[1]);
        Assert.Equal(before[2], records[2]);
        Assert.Equal(before[3] with { AcknowledgedAtUtc = Now.AddSeconds(1) }, records[3]);
        Assert.Equal(before[4], records[4]);
        Assert.Equal(new uint[] { 100_000, 100_002, 100_003 }, tracking.PendingItemIds.Order());
    }

    [Fact]
    public void InvalidTrackingAndEmptyRecordsNeverReconcile()
    {
        // Arrange
        List<PendingPurchase> records = [new(Character, 0, Now)];
        var invalid = Prepare(records);

        // Act
        var changed = PurchaseTrackingPolicy.Reconcile(invalid, records, Character, ExportaramusFixture.State());
        var emptyChanged = PurchaseTrackingPolicy.Reconcile(Prepare([]), [], Character, ExportaramusFixture.State());

        // Assert
        Assert.False(changed);
        Assert.False(emptyChanged);
        Assert.Equal(new PendingPurchase(Character, 0, Now), Assert.Single(records));
    }

    [Fact]
    public void InvalidTrackingBlocksOtherwiseObservableValidRecords()
    {
        // Arrange
        List<PendingPurchase> records = [new(Character, 100_000, Now), new(Character, 0, Now)];
        var before = records.ToArray();
        var tracking = Prepare(records);
        var ownership = ObservedOwnership();

        // Act
        var changed = PurchaseTrackingPolicy.Reconcile(tracking, records, Character, ownership);

        // Assert
        Assert.False(changed);
        Assert.Equal(before, records);
    }

    [Fact]
    public void ForeignSnapshotCannotAcknowledgeRecordsOutsideTheCurrentCharacter()
    {
        // Arrange
        List<PendingPurchase> records = [new(Character, 100_000, Now)];
        var before = records.ToArray();
        var tracking = PurchaseTrackingPolicy.Prepare(records, new HashSet<uint>(), 42, Now);
        var foreignOwnership = ObservedOwnership();

        // Act
        var changed = PurchaseTrackingPolicy.Reconcile(tracking, records, 42, foreignOwnership);

        // Assert
        Assert.False(changed);
        Assert.Equal(before, records);
    }

    [Fact]
    public void BelowCapacityAllowsNewMissingItemWithoutAnExistingRecord()
    {
        // Arrange
        List<PendingPurchase> records = [new(Character, 1, Now)];
        var tracking = Prepare(records);

        // Act
        var readiness = PurchaseTrackingPolicy.Readiness(tracking, records, Item, Character, Character,
            ExportaramusFixture.State());

        // Assert
        Assert.Equal(new ItemOwnership(true, false, false,
            "Complete observations contain neither this item nor its unlock."), readiness);
    }

    [Fact]
    public void OwnershipDecorationRetainsProviderFactsAndRejectsWrongCharacters()
    {
        // Arrange
        var source = ExportaramusFixture.State();
        var tracking = PurchaseTrackingPolicy.Prepare([new(Character, 2, Now, Now)], new HashSet<uint> { 2, 3 }, Character, Now);

        // Act
        var decorated = PurchaseTrackingPolicy.ApplyOwnership(tracking, Character, source);
        var wrongCharacter = PurchaseTrackingPolicy.ApplyOwnership(tracking, 42, source);
        var unavailable = PurchaseTrackingPolicy.ApplyOwnership(tracking, Character, CollectionDataOwnershipPolicy.NotChecked);
        var invalid = PurchaseTrackingPolicy.ApplyOwnership(Prepare([new(Character, 0, Now)]), Character, source);

        // Assert
        Assert.Equal(source with { AcknowledgedAcquisitions = tracking.AcknowledgedAcquisitions,
            LegacyPurchaseArchive = tracking.LegacyPurchaseArchive }, decorated);
        Assert.Equal(CollectionDataOwnershipPolicy.NotChecked, wrongCharacter);
        Assert.Null(unavailable.Snapshot);
        Assert.Equal(tracking.AcknowledgedAcquisitions, unavailable.AcknowledgedAcquisitions);
        Assert.Equal(tracking.LegacyPurchaseArchive, unavailable.LegacyPurchaseArchive);
        Assert.Equal(CollectionDataOwnershipPolicy.NotChecked with {
            StatusDetail = "Saved purchase tracking is invalid. Purchases are blocked; existing records have been preserved." }, invalid);
        Assert.Empty(source.AcknowledgedAcquisitions);
        Assert.Empty(source.LegacyPurchaseArchive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapacityAllowsOnlyReplacingTheSameCharactersExistingRecord(bool otherCharacterOwnsItem)
    {
        // Arrange
        var records = Enumerable.Range(1, PendingPurchaseGuard.MaximumRecords)
            .Select(index => new PendingPurchase(Character, (uint)index, Now, Now)).ToList();
        records[0] = new(otherCharacterOwnsItem ? 42UL : Character, Item.ItemId, Now, Now);
        var tracking = Prepare(records);

        // Act
        var readiness = PurchaseTrackingPolicy.Readiness(tracking, records, Item, Character, Character, ExportaramusFixture.State());

        // Assert
        Assert.Equal(otherCharacterOwnsItem
            ? new ItemOwnership(false, false, false, "Purchase tracking has reached its safety limit.")
            : new ItemOwnership(true, false, false, "Complete observations contain neither this item nor its unlock."), readiness);
    }

    [Fact]
    public void BelowCapacityUnknownIdentityAndPendingPurchaseRemainIndependent()
    {
        // Arrange
        List<PendingPurchase> records = [new(Character, Item.ItemId, Now)];
        var tracking = Prepare(records);

        // Act
        var pending = PurchaseTrackingPolicy.Readiness(tracking, records, Item, Character, Character, ExportaramusFixture.State());
        var changed = PurchaseTrackingPolicy.Readiness(tracking, records, Item, Character, 42, ExportaramusFixture.State());
        var invalid = PurchaseTrackingPolicy.Readiness(Prepare([new(Character, 0, Now)]), records, Item, Character, Character, ExportaramusFixture.State());

        // Assert
        Assert.Equal(new ItemOwnership(true, true, false, "Purchase confirmed; waiting for Exportaramus to observe the item."), pending);
        Assert.Equal(new ItemOwnership(false, false, false, "Character identity changed or is unavailable."), changed);
        Assert.Equal(new ItemOwnership(false, false, false, "Saved purchase tracking is invalid; purchases are blocked."), invalid);
    }

    [Fact]
    public void ConfirmedPurchaseReplacesOnlyItsCharacterItemPairAndRetainsHistory()
    {
        // Arrange
        List<PendingPurchase> records = [new(Character, 1, Now, Now), new(42, 1, Now), new(Character, 2, Now)];
        HashSet<uint> archive = [1, 2];

        // Act
        var changed = PurchaseTrackingPolicy.RecordConfirmed(records, archive, 1, Character, Now.AddSeconds(1));

        // Assert
        Assert.True(changed);
        Assert.Equal(new PendingPurchase[] { new(42, 1, Now), new(Character, 2, Now), new(Character, 1, Now.AddSeconds(1)) }, records);
        Assert.True(archive.SetEquals([1, 2]));
        Assert.Equal(new uint[] { 1, 2 }, Prepare(records, Now.AddSeconds(1)).PendingItemIds.Order());
    }

    [Theory]
    [InlineData(0u, false)]
    [InlineData(3u, true)]
    public void ConfirmationPreservesZeroItemNoOpAndRecordsNonzeroItems(uint itemId, bool expectedChange)
    {
        // Arrange
        List<PendingPurchase> records = [];
        HashSet<uint> archive = [];

        // Act
        var changed = PurchaseTrackingPolicy.RecordConfirmed(records, archive, itemId, 0, Now);

        // Assert
        Assert.Equal(expectedChange, changed);
        Assert.Equal(expectedChange ? 1 : 0, records.Count);
        Assert.Equal(expectedChange, archive.Contains(itemId));
        if (expectedChange) Assert.False(Prepare(records).IsValid);
        else Assert.Empty(archive);
    }

    private static PurchaseTrackingState Prepare(IReadOnlyList<PendingPurchase> records, DateTimeOffset? now = null)
        => PurchaseTrackingPolicy.Prepare(records, new HashSet<uint>(), Character, now ?? Now);

    private static CollectionDataOwnershipState ObservedOwnership()
    {
        var snapshot = ExportaramusFixture.Snapshot();
        return ExportaramusFixture.State(snapshot with {
            CurrentItemPresence = snapshot.CurrentItemPresence.SetItem("inventory", new([new(100_000, 1)])),
            SourceHealth = snapshot.SourceHealth.SetItem("inventory", snapshot.SourceHealth["inventory"] with {
                ObservedAtUtc = Now.AddSeconds(1).ToString("O") }),
        });
    }
}
