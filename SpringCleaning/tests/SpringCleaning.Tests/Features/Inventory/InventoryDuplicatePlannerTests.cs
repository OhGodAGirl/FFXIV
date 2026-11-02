namespace SpringCleaning.Tests;

public sealed class InventoryDuplicatePlannerTests
{
    [Fact]
    public void GetDispositionOffersRemovalWhenOnlyArmoireHasAppearance()
    {
        var disposition = InventoryDuplicatePlanner.GetDisposition(true, false);

        Assert.Equal(InventoryDuplicateDisposition.RemovalReview, disposition);
    }

    [Fact]
    public void GetDispositionOffersRemovalReviewOnlyWhenBothStoresHaveAppearance()
    {
        var disposition = InventoryDuplicatePlanner.GetDisposition(true, true);

        Assert.Equal(InventoryDuplicateDisposition.RemovalReview, disposition);
    }

    [Fact]
    public void GetDispositionOffersRemovalReviewWhenOnlyDresserHasAppearance()
    {
        var disposition = InventoryDuplicatePlanner.GetDisposition(false, true);

        Assert.Equal(InventoryDuplicateDisposition.RemovalReview, disposition);
    }

    [Fact]
    public void GetDispositionIgnoresItemsNotRepresentedInEitherStore()
    {
        var disposition = InventoryDuplicatePlanner.GetDisposition(false, false);

        Assert.Equal(InventoryDuplicateDisposition.None, disposition);
    }

    [Fact]
    public void BulkDiscardAllowsOnlySafeRemovalCopies()
    {
        // Arrange
        var candidate = Duplicate(
            new InventoryItemSafety(true, true, "Ready"),
            InventoryDuplicateDisposition.RemovalReview);

        // Act
        var allowed = InventoryDuplicatePlanner.CanBulkDiscard(candidate);

        // Assert
        Assert.True(allowed);
    }

    [Theory]
    [InlineData(false, (int)InventoryDuplicateDisposition.RemovalReview)]
    [InlineData(true, (int)InventoryDuplicateDisposition.DresserFallback)]
    [InlineData(true, (int)InventoryDuplicateDisposition.None)]
    public void BulkDiscardRejectsUnsafeOrNonRemovalItems(
        bool safe,
        int dispositionValue)
    {
        // Arrange
        var disposition = (InventoryDuplicateDisposition)dispositionValue;
        var candidate = Duplicate(
            new InventoryItemSafety(true, safe, safe ? "Ready" : "Blocked"),
            disposition);

        // Act
        var allowed = InventoryDuplicatePlanner.CanBulkDiscard(candidate);

        // Assert
        Assert.False(allowed);
    }

    [Fact]
    public void BulkDiscardRejectsAnInconsistentMissingCandidateEvenWhenMarkedSafe()
    {
        // Arrange
        var candidate = Duplicate(
            new InventoryItemSafety(false, true, "Stale snapshot"),
            InventoryDuplicateDisposition.RemovalReview);

        // Act
        var allowed = InventoryDuplicatePlanner.CanBulkDiscard(candidate);

        // Assert
        Assert.False(allowed);
    }

    [Fact]
    public void BulkDiscardSelectionPreservesOrderAndExcludesUnsafeOrFallbackItems()
    {
        // Arrange
        var first = Duplicate(
            new InventoryItemSafety(true, true, "Ready"),
            InventoryDuplicateDisposition.RemovalReview) with
        { ItemId = 10, Name = "First" };
        var unsafeCopy = Duplicate(
            new InventoryItemSafety(true, false, "Blocked"),
            InventoryDuplicateDisposition.RemovalReview) with
        { ItemId = 11, Name = "Unsafe" };
        var fallback = Duplicate(
            new InventoryItemSafety(true, true, "Ready"),
            InventoryDuplicateDisposition.DresserFallback) with
        { ItemId = 12, Name = "Fallback" };
        var last = Duplicate(
            new InventoryItemSafety(true, true, "Ready"),
            InventoryDuplicateDisposition.RemovalReview) with
        { ItemId = 13, Name = "Last" };

        // Act
        var selected = InventoryDuplicatePlanner.SelectBulkDiscardCandidates(
            [first, unsafeCopy, fallback, last]);

        // Assert
        Assert.Equal([10U, 13U], selected.Select(candidate => candidate.ItemId));
    }

    private static InventoryDuplicateItem Duplicate(
        InventoryItemSafety safety,
        InventoryDuplicateDisposition disposition) => new(
            0,
            1,
            2,
            false,
            "Duplicate",
            3,
            safety,
            false,
            true,
            disposition);
}
