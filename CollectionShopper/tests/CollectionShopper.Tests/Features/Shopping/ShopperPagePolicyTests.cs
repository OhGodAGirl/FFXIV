namespace CollectionShopper.Tests;

/// <summary>
/// Verifies category-tab visibility stays independent from combined navigation.
/// </summary>
public sealed class ShopperPagePolicyTests
{
    [Fact]
    public void EveryCategoryPageKeepsItsPurchaseScopeIndependentOfCombinedSelections()
    {
        var categories = Enum.GetValues<ShoppingCategory>();
        var pages = new[]
        {
            ShopperPage.Outfits, ShopperPage.Weapons, ShopperPage.Minions, ShopperPage.Mounts,
            ShopperPage.Orchestrions, ShopperPage.Hairstyles, ShopperPage.Facewear, ShopperPage.Fashion,
        };
        for (var mask = 0; mask < 256; mask++)
        {
            var enabled = categories.Where((_, index) => (mask & (1 << index)) != 0).ToHashSet();
            for (var index = 0; index < pages.Length; index++)
            {
                var page = pages[index];
                var ownCategory = categories[index];
                foreach (var category in categories)
                {
                    Assert.Equal(category == ownCategory,
                        ShopperPagePolicy.IncludesPurchaseCategory(page, category,
                            _ => throw new InvalidOperationException("Individual pages must not consult combined selections.")));
                    Assert.Equal(enabled.Contains(category),
                        ShopperPagePolicy.IncludesPurchaseCategory(ShopperPage.AllMissing, category, enabled.Contains));
                    Assert.False(ShopperPagePolicy.IncludesPurchaseCategory(ShopperPage.Settings, category,
                        _ => throw new InvalidOperationException("Settings must not consult purchase selections.")));
                }

                Assert.Equal(enabled.Contains(ownCategory) ? page : ShopperPage.AllMissing,
                    ShopperPagePolicy.KeepVisible(page, enabled.Contains));
            }
        }
    }

    /// <summary>
    /// Verifies each category page maps to its corresponding shopping category.
    /// </summary>
    [Theory]
    [InlineData((int)ShopperPage.Outfits, (int)ShoppingCategory.Outfit)]
    [InlineData((int)ShopperPage.Weapons, (int)ShoppingCategory.Weapon)]
    [InlineData((int)ShopperPage.Minions, (int)ShoppingCategory.Minion)]
    [InlineData((int)ShopperPage.Mounts, (int)ShoppingCategory.Mount)]
    [InlineData((int)ShopperPage.Orchestrions, (int)ShoppingCategory.Orchestrion)]
    [InlineData((int)ShopperPage.Hairstyles, (int)ShoppingCategory.Hairstyle)]
    [InlineData((int)ShopperPage.Facewear, (int)ShoppingCategory.Facewear)]
    [InlineData((int)ShopperPage.Fashion, (int)ShoppingCategory.Fashion)]
    public void CategoryPagesMapToTheirCategory(int pageValue, int expectedValue)
    {
        // Arrange
        var page = (ShopperPage)pageValue;
        var expected = (ShoppingCategory)expectedValue;

        // Act
        var actual = ShopperPagePolicy.GetCategory(page);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies hiding the active category tab returns navigation to All Missing.
    /// </summary>
    [Fact]
    public void HiddenActiveCategoryFallsBackToAllMissing()
    {
        // Arrange
        const ShopperPage activePage = ShopperPage.Mounts;

        // Act
        var actual = ShopperPagePolicy.KeepVisible(activePage, _ => false);

        // Assert
        Assert.Equal(ShopperPage.AllMissing, actual);
    }

    /// <summary>
    /// Verifies fixed navigation pages are unaffected by category visibility.
    /// </summary>
    [Theory]
    [InlineData((int)ShopperPage.AllMissing)]
    [InlineData((int)ShopperPage.Settings)]
    public void FixedPagesRemainVisible(int activePageValue)
    {
        // Arrange
        var activePage = (ShopperPage)activePageValue;

        // Act
        var actual = ShopperPagePolicy.KeepVisible(activePage, _ => false);

        // Assert
        Assert.Equal(activePage, actual);
    }

    /// <summary>
    /// Verifies an individual category tab buys its category regardless of the All Missing selection.
    /// </summary>
    [Fact]
    public void CategoryPageIncludesItsOwnPurchases()
    {
        // Arrange
        const ShopperPage page = ShopperPage.Mounts;

        // Act
        var included = ShopperPagePolicy.IncludesPurchaseCategory(
            page,
            ShoppingCategory.Mount,
            _ => false);

        // Assert
        Assert.True(included);
    }

    /// <summary>
    /// Verifies All Missing includes only categories selected for automated purchasing.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AllMissingUsesAutomatedPurchaseSelection(bool enabled)
    {
        // Arrange
        const ShopperPage page = ShopperPage.AllMissing;

        // Act
        var included = ShopperPagePolicy.IncludesPurchaseCategory(
            page,
            ShoppingCategory.Outfit,
            _ => enabled);

        // Assert
        Assert.Equal(enabled, included);
    }

    /// <summary>
    /// Verifies Settings never becomes a purchase surface.
    /// </summary>
    [Fact]
    public void SettingsDoesNotIncludePurchases()
    {
        // Arrange
        const ShopperPage page = ShopperPage.Settings;

        // Act
        var included = ShopperPagePolicy.IncludesPurchaseCategory(
            page,
            ShoppingCategory.Outfit,
            _ => true);

        // Assert
        Assert.False(included);
    }
}
