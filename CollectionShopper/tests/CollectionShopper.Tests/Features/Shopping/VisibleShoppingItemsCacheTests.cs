namespace CollectionShopper.Tests;

public sealed class VisibleShoppingItemsCacheTests
{
    [Fact]
    public void UnchangedInputsReuseTheProjectionWithoutEnumeratingAgain()
    {
        // Arrange
        var cache = new VisibleShoppingItemsCache();
        var key = new VisibleShoppingItemsCache.Key(0, 0, ShopperPage.Outfits, "", true, false, 0);
        var first = cache.Get(key, [ShoppingListPolicyTests.Item(1)], new HashSet<uint>());
        // Act
        var second = cache.Get(key, ThrowOnEnumeration(), new HashSet<uint>());
        // Assert
        Assert.Same(first, second);
        Assert.Equal(1u, Assert.Single(second).ItemId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void EachRelevantInputInvalidatesTheProjection(int field)
    {
        // Arrange
        var cache = new VisibleShoppingItemsCache();
        var key = new VisibleShoppingItemsCache.Key(0, 0, ShopperPage.Outfits, "", true, false, 0);
        var first = cache.Get(key, [ShoppingListPolicyTests.Item(1)], new HashSet<uint>());
        var changed = field switch
        {
            0 => key with { ItemsRevision = 1 },
            1 => key with { CompletionRevision = 1 },
            2 => key with { Page = ShopperPage.AllMissing },
            3 => key with { Filter = "Item" },
            4 => key with { HideLocallyOwned = false },
            5 => key with { AllowHighQuality = true },
            _ => key with { CurrentItemId = 2 },
        };
        // Act
        var second = cache.Get(changed, [ShoppingListPolicyTests.Item(2)], new HashSet<uint>());
        // Assert
        Assert.NotSame(first, second);
        Assert.Equal(2u, Assert.Single(second).ItemId);
    }

    [Fact]
    public void ConfirmedPurchaseInvalidationRemovesThePurchasedItem()
    {
        // Arrange
        var cache = new VisibleShoppingItemsCache();
        var key = new VisibleShoppingItemsCache.Key(0, 0, ShopperPage.Outfits, "", true, false, 0);
        ShoppingItem[] items = [ShoppingListPolicyTests.Item(1), ShoppingListPolicyTests.Item(2)];
        cache.Get(key, items, new HashSet<uint>());
        // Act
        var visible = cache.Get(key with { CompletionRevision = 1 }, items, new HashSet<uint> { 1 });
        // Assert
        Assert.Equal(2u, Assert.Single(visible).ItemId);
    }

    private static IEnumerable<ShoppingItem> ThrowOnEnumeration()
    {
        yield return Throw();
        static ShoppingItem Throw() => throw new InvalidOperationException("Cached projections must not enumerate.");
    }
}
