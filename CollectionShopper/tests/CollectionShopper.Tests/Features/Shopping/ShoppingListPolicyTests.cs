namespace CollectionShopper.Tests;

public sealed class ShoppingListPolicyTests
{
    [Theory]
    [InlineData(false, 2, 1, 20ul)]
    [InlineData(true, 2, 2, 25ul)]
    public void SummaryCountsUnpricedMissingItemsWithoutInventingPrices(bool hq, int count, int priced, ulong total)
    {
        // Arrange
        ShoppingItem[] items = [Item(1, price: 20), Item(2) with { Prices = new(null, new(2, 5, 1, "Mateus", true)) },
            Item(3) with { OwnershipKnown = false }, Item(4) with { IsLocallyOwned = true },
            Item(5), Item(6) with { Category = ShoppingCategory.Mount }];
        // Act
        var summary = ShoppingListPolicy.Summarize(items, ShopperPage.AllMissing,
            category => category == ShoppingCategory.Outfit, new HashSet<uint> { 5 }, hq);
        // Assert
        Assert.Equal(new ShoppingListPolicy.Summary(count, priced, total), summary);
    }

    [Fact]
    public void PanelSummaryUsesSuppliedRowsWithoutApplyingGlobalBuyAllFilters()
    {
        // Arrange
        ShoppingItem[] items = [Item(1, price: uint.MaxValue), Item(2, price: uint.MaxValue) with { IsLocallyOwned = true }];
        // Act
        var summary = ShoppingListPolicy.Summarize(items, null, _ => false, new HashSet<uint> { 1 }, false);
        // Assert
        Assert.Equal(new ShoppingListPolicy.Summary(2, 2, 2UL * uint.MaxValue), summary);
    }

    [Theory]
    [InlineData(false, 0, 0, false)]
    [InlineData(true, 0, 0, true)]
    [InlineData(true, 1, 0, false)]
    [InlineData(true, 2, 1, true)]
    public void SummaryDoesNotPresentUnknownOwnershipOrUnpricedItemsAsZeroCost(bool ready, int count, int priced, bool expected)
    {
        // Arrange
        var summary = new ShoppingListPolicy.Summary(count, priced, 0);
        // Act
        var result = summary.ShowEstimate(ready);
        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(true, "Checking")]
    [InlineData(false, "Not Found")]
    public void MissingQuoteCopyDoesNotClaimLiveUnavailability(bool checking, string expected)
    {
        // Arrange
        var item = Item(1);
        // Act
        var label = ShoppingListPolicy.MissingPriceLabel(checking);
        var canBuy = ShoppingListPolicy.CanBuy(item, new HashSet<uint>(), false);
        // Assert
        Assert.Equal(expected, label);
        Assert.Equal("No Universalis Price - Check Marketboard", ShoppingListPolicy.MissingPriceTooltip);
        Assert.True(canBuy);
    }

    [Theory]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(true, false, false, true, false)]
    [InlineData(true, false, false, false, true)]
    public void IndividualBuyRequiresKnownMissingAndNoActiveRun(bool known, bool owned, bool pending, bool running, bool expected)
    {
        // Arrange
        var item = Item(1) with { OwnershipKnown = known, IsLocallyOwned = owned };
        var suppressed = pending ? new HashSet<uint> { 1 } : [];
        // Act
        var result = ShoppingListPolicy.CanBuy(item, suppressed, running);
        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuyAllUsesCategoryThenQualityAwarePriceThenNameAndKeepsUnpricedItems(bool allowHq)
    {
        // Arrange
        var hq = Item(4, "HQ") with { Prices = new(null, new(4, 5, 1, "Mateus", true)) };
        ShoppingItem[] items = [Item(8) with { OwnershipKnown = false }, Item(9) with { IsLocallyOwned = true },
            Item(10), Item(11) with { Category = ShoppingCategory.Mount },
            Item(3, "Z", 10), Item(2, "A", 10), Item(1, "Unpriced"), hq,
            Item(5, "Priority", 100) with { CollectionPriority = -1 }];
        // Act
        var result = ShoppingListPolicy.PurchaseItems(items, ShopperPage.AllMissing,
            category => category == ShoppingCategory.Outfit, new HashSet<uint> { 10 }, allowHq).Select(item => item.ItemId).ToArray();
        // Assert
        Assert.Equal(allowHq ? new uint[] { 5, 4, 2, 3, 1 } : new uint[] { 5, 2, 3, 4, 1 }, result);
    }

    [Theory]
    [InlineData(true, false, 1, "Ready", true)]
    [InlineData(false, true, 1, "Ready", true)]
    [InlineData(false, false, 0, "Ready", true)]
    [InlineData(false, false, 1, "Purchased Coat.", true)]
    [InlineData(false, false, 1, "Refresh FAILED.", true)]
    [InlineData(false, false, 1, "Shopping CANCELLED.", true)]
    [InlineData(false, false, 1, "Ready", false)]
    [InlineData(false, false, 1, "purchased Coat.", false)]
    public void StatusVisibilityPreservesCurrentCopyRules(bool refreshing, bool running, int count, string status, bool expected)
    {
        // Arrange / Act
        var result = ShoppingListPolicy.ShowStatus(refreshing, running, count, status);
        // Assert
        Assert.Equal(expected, result);
    }

    internal static ShoppingItem Item(uint id, string name = "Item", uint? price = null)
        => new(id, name, 1, "Collection", null, "", "", 1, true, false,
            price is { } value ? new(new(id, value, 1, "Mateus", false), null) : null,
            ShoppingCategory.Outfit, 0, 1);
}
