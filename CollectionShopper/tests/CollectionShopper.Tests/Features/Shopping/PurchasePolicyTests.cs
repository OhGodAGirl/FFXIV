namespace CollectionShopper.Tests;

/// <summary>
/// Verifies deterministic listing selection and spending policy.
/// </summary>
public sealed class PurchasePolicyTests
{
    /// <summary>
    /// Verifies malformed, mismatched, multi-quantity, mannequin, and disallowed HQ listings are rejected.
    /// </summary>
    [Fact]
    public void SelectBestListingRejectsUnsafeListings()
    {
        // Arrange
        var listings = new[]
        {
            Listing(index: 0, itemId: 99),
            Listing(index: 1, listingId: 0),
            Listing(index: 2, unitPrice: 0),
            Listing(index: 3, quantity: 2),
            Listing(index: 4, isMannequin: true),
            Listing(index: 5, isHighQuality: true),
            Listing(index: 6, unitPrice: 2_000),
        };

        // Act
        var selected = PurchasePolicy.SelectBestListing(listings, 42, allowHighQuality: false);

        // Assert
        Assert.NotNull(selected);
        Assert.Equal(6, selected.Value.SourceIndex);
    }

    /// <summary>
    /// Verifies NQ remains preferred when HQ purchases are permitted.
    /// </summary>
    [Fact]
    public void SelectBestListingPrefersNormalQualityWhenHighQualityIsAllowed()
    {
        // Arrange
        var listings = new[]
        {
            Listing(index: 0, unitPrice: 500, isHighQuality: true),
            Listing(index: 1, unitPrice: 1_000),
        };

        // Act
        var selected = PurchasePolicy.SelectBestListing(listings, 42, allowHighQuality: true);

        // Assert
        Assert.NotNull(selected);
        Assert.Equal(1, selected.Value.SourceIndex);
    }

    /// <summary>
    /// Verifies HQ is selected only as an explicitly allowed fallback.
    /// </summary>
    [Fact]
    public void SelectBestListingUsesHighQualityOnlyAsAllowedFallback()
    {
        // Arrange
        var listings = new[] { Listing(index: 0, isHighQuality: true) };

        // Act
        var selected = PurchasePolicy.SelectBestListing(listings, 42, allowHighQuality: true);

        // Assert
        Assert.NotNull(selected);
        Assert.True(selected.Value.IsHighQuality);
    }

    /// <summary>
    /// Verifies tax participates in cheapest-listing selection.
    /// </summary>
    [Fact]
    public void SelectBestListingUsesTaxInTotalCostOrdering()
    {
        // Arrange
        var listings = new[]
        {
            Listing(index: 0, unitPrice: 900, totalTax: 200),
            Listing(index: 1, unitPrice: 1_000, totalTax: 50),
        };

        // Act
        var selected = PurchasePolicy.SelectBestListing(listings, 42, allowHighQuality: false);

        // Assert
        Assert.NotNull(selected);
        Assert.Equal(1, selected.Value.SourceIndex);
        Assert.Equal(1_050UL, selected.Value.TotalCost);
    }

    /// <summary>
    /// Verifies stable source order is retained when eligible totals are equal.
    /// </summary>
    [Fact]
    public void SelectBestListingPreservesSourceOrderForEqualTotals()
    {
        // Arrange
        var listings = new[]
        {
            Listing(index: 7, unitPrice: 1_000, totalTax: 50),
            Listing(index: 3, unitPrice: 1_025, totalTax: 25),
        };

        // Act
        var selected = PurchasePolicy.SelectBestListing(listings, 42, allowHighQuality: false);

        // Assert
        Assert.NotNull(selected);
        Assert.Equal(7, selected.Value.SourceIndex);
    }

    /// <summary>
    /// Verifies no listing is returned when every candidate is unsafe.
    /// </summary>
    [Fact]
    public void SelectBestListingReturnsNullWhenEveryListingIsUnsafe()
    {
        // Arrange
        var listings = new[]
        {
            Listing(index: 0, quantity: 2),
            Listing(index: 1, isMannequin: true),
            Listing(index: 2, isHighQuality: true),
        };

        // Act
        var selected = PurchasePolicy.SelectBestListing(listings, 42, allowHighQuality: false);

        // Assert
        Assert.Null(selected);
    }

    /// <summary>
    /// Verifies the per-item spending cap includes tax at the exact boundary.
    /// </summary>
    [Fact]
    public void IsWithinPerItemCapIncludesTaxAtBoundary()
    {
        // Arrange
        var listing = Listing(index: 0, unitPrice: 1_000, totalTax: 51);

        // Act
        var belowTotal = PurchasePolicy.IsWithinPerItemCap(listing, 1_050, unlimitedPricePerItem: false);
        var exactTotal = PurchasePolicy.IsWithinPerItemCap(listing, 1_051, unlimitedPricePerItem: false);

        // Assert
        Assert.False(belowTotal);
        Assert.True(exactTotal);
    }

    /// <summary>
    /// Verifies a positive per-item cap is mandatory.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void IsWithinPerItemCapRejectsNonPositiveCap(int maximumPricePerItem)
    {
        // Arrange
        var listing = Listing(index: 0, unitPrice: 1);

        // Act
        var allowed = PurchasePolicy.IsWithinPerItemCap(
            listing,
            maximumPricePerItem,
            unlimitedPricePerItem: false);

        // Assert
        Assert.False(allowed);
    }

    /// <summary>
    /// Verifies a zero cap remains disabled even for a zero-cost malformed candidate.
    /// </summary>
    [Fact]
    public void IsWithinPerItemCapRejectsZeroCapForZeroCostCandidate()
    {
        var listing = Listing(index: 0, unitPrice: 0);

        Assert.False(PurchasePolicy.IsWithinPerItemCap(listing, 0, false));
    }

    /// <summary>
    /// Verifies unlimited per-item spending bypasses only the item cap calculation.
    /// </summary>
    [Fact]
    public void IsWithinPerItemCapAllowsListingWhenUnlimited()
    {
        // Arrange
        var listing = Listing(index: 0, unitPrice: 1_000, totalTax: 50);

        // Act
        var allowed = PurchasePolicy.IsWithinPerItemCap(
            listing,
            maximumPricePerItem: 0,
            unlimitedPricePerItem: true);

        // Assert
        Assert.True(allowed);
    }

    /// <summary>
    /// Verifies the cumulative cap includes tax and prior confirmed spending.
    /// </summary>
    [Fact]
    public void IsWithinTotalCapIncludesTaxAndPriorSpend()
    {
        // Arrange
        var listing = Listing(index: 0, unitPrice: 1_000, totalTax: 50);

        // Act
        var belowTotal = PurchasePolicy.IsWithinTotalCap(5_000, listing, 6_049, unlimitedTotalSpend: false);
        var exactTotal = PurchasePolicy.IsWithinTotalCap(5_000, listing, 6_050, unlimitedTotalSpend: false);
        var overflowAttempt = PurchasePolicy.IsWithinTotalCap(
            ulong.MaxValue - 500,
            listing,
            int.MaxValue,
            unlimitedTotalSpend: false);

        // Assert
        Assert.False(belowTotal);
        Assert.True(exactTotal);
        Assert.False(overflowAttempt);
    }

    /// <summary>
    /// Verifies unlimited total spend bypasses only the run cap calculation.
    /// </summary>
    [Fact]
    public void IsWithinTotalCapAllowsLargeConfirmedSpendWhenUnlimited()
    {
        // Arrange
        var listing = Listing(index: 0, unitPrice: 1_000, totalTax: 50);

        // Act
        var allowed = PurchasePolicy.IsWithinTotalCap(
            ulong.MaxValue - listing.TotalCost,
            listing,
            maximumTotalSpend: 0,
            unlimitedTotalSpend: true);

        // Assert
        Assert.True(allowed);
    }

    /// <summary>
    /// Verifies a finite total-spend cap must be positive.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void IsWithinTotalCapRejectsNonPositiveFiniteCap(int maximumTotalSpend)
    {
        // Arrange
        var listing = Listing(index: 0, unitPrice: 1);

        // Act
        var allowed = PurchasePolicy.IsWithinTotalCap(
            alreadySpent: 0,
            listing,
            maximumTotalSpend,
            unlimitedTotalSpend: false);

        // Assert
        Assert.False(allowed);
    }

    /// <summary>
    /// Verifies a zero run cap remains disabled even for a zero-cost malformed candidate.
    /// </summary>
    [Fact]
    public void IsWithinTotalCapRejectsZeroCapForZeroCostCandidate()
    {
        var listing = Listing(index: 0, unitPrice: 0);

        Assert.False(PurchasePolicy.IsWithinTotalCap(0, listing, 0, false));
    }

    /// <summary>
    /// Verifies a listing equal to the entire run cap is allowed at the exact boundary.
    /// </summary>
    [Fact]
    public void IsWithinTotalCapAllowsListingEqualToEntireCap()
    {
        var listing = Listing(index: 0, unitPrice: 1_000, totalTax: 50);

        Assert.True(PurchasePolicy.IsWithinTotalCap(0, listing, 1_050, false));
    }

    /// <summary>
    /// Verifies run preparation removes owned, completed, and duplicate items while
    /// preserving the first eligible source order.
    /// </summary>
    [Fact]
    public void PrepareRunFiltersUnsafeItemsAndPreservesFirstOccurrence()
    {
        // Arrange
        var available = ShoppingItem(itemId: 10);
        var duplicate = available with { ItemName = "Duplicate" };
        var locallyOwned = ShoppingItem(itemId: 11) with { IsLocallyOwned = true };
        var completed = ShoppingItem(itemId: 12);
        var ownershipUnknown = ShoppingItem(itemId: 13) with { OwnershipKnown = false };

        // Act
        var prepared = PurchasePolicy.PrepareRun(
            [available, duplicate, locallyOwned, completed, ownershipUnknown],
            new HashSet<uint> { 12 }).ToArray();

        // Assert
        var selected = Assert.Single(prepared);
        Assert.Equal(10U, selected.ItemId);
        Assert.Equal("Item 10", selected.ItemName);
    }

    /// <summary>
    /// Verifies an empty run remains empty without requiring completed-item state.
    /// </summary>
    [Fact]
    public void PrepareRunReturnsEmptyForEmptyCandidates()
    {
        // Arrange
        var completed = new HashSet<uint>();

        // Act
        var prepared = PurchasePolicy.PrepareRun([], completed);

        // Assert
        Assert.Empty(prepared);
    }

    private static PurchaseListingCandidate Listing(
        int index,
        uint itemId = 42,
        ulong listingId = 1,
        uint unitPrice = 1_000,
        uint totalTax = 0,
        uint quantity = 1,
        bool isHighQuality = false,
        bool isMannequin = false)
        => new(
            index,
            itemId,
            listingId,
            unitPrice,
            totalTax,
            quantity,
            isHighQuality,
            isMannequin);

    private static ShoppingItem ShoppingItem(uint itemId)
        => new(
            itemId,
            $"Item {itemId}",
            CollectionId: 1,
            CollectionName: "Example Collection",
            CollectionIconPath: null,
            Patch: "7.4",
            Source: "Example Source",
            IconId: 0,
            OwnershipKnown: true,
            IsLocallyOwned: false,
            Prices: null,
            Category: ShoppingCategory.Outfit,
            CollectionPriority: 0,
            RemainingPiecesInCollection: 1);
}
