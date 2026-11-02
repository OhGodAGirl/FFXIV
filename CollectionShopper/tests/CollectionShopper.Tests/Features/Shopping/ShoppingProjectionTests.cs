namespace CollectionShopper.Tests;

public sealed class ShoppingProjectionTests
{
    [Theory]
    [InlineData(true, 3, 2, "Partial (1 unpriced)")]
    [InlineData(true, 3, 3, "Estimated")]
    [InlineData(true, 3, 0, "Estimate unavailable")]
    [InlineData(false, 3, 3, "Estimate unavailable")]
    [InlineData(true, 0, 0, "Estimated")]
    public void EstimateLabelsDistinguishPartialMissingAndCompletePrices(bool ready, int count, int priced, string expected)
        => Assert.Equal(expected, ShoppingProjection.EstimateLabel(ready, count, priced));

    [Fact]
    public void PricesIncludeOnlyVerifiedMissingItemsAcrossCategories()
    {
        var missing = Item(3, "Missing", ShoppingCategory.Outfit, 0, 30);
        var weapon = Item(6, "Weapon", ShoppingCategory.Weapon, 0, 30);
        Assert.Equal(new uint[] { 3, 6 }, ShoppingProjection.PriceItemIds(
            [missing with { ItemId = 2, OwnershipKnown = false }, missing, missing,
             missing with { ItemId = 0 }, missing with { ItemId = 4 },
             missing with { ItemId = 5, IsLocallyOwned = true }, weapon,
             missing with { ItemId = 7, MappingValid = false }], new HashSet<uint> { 4 }));
        Assert.Empty(ShoppingProjection.PriceItemIds([], new HashSet<uint>()));
        Assert.Equal("items", Assert.Throws<ArgumentNullException>(() => ShoppingProjection.PriceItemIds(null!, new HashSet<uint>())).ParamName);
        Assert.Throws<ArgumentNullException>(() => ShoppingProjection.PriceItemIds([], null!));
    }

    [Fact]
    public void PriceRequestsFollowOwnershipRecoveryAndConfirmedPurchases()
    {
        var item = Item(3, "Missing", ShoppingCategory.Outfit, 0, 30);
        Assert.Empty(ShoppingProjection.PriceItemIds([item with { OwnershipKnown = false }], new HashSet<uint>()));
        Assert.Equal(new uint[] { 3 }, ShoppingProjection.PriceItemIds([item], new HashSet<uint>()));
        Assert.Empty(ShoppingProjection.PriceItemIds([item], new HashSet<uint> { 3 }));
        Assert.Empty(ShoppingProjection.PriceItemIds([item with { IsLocallyOwned = true }], new HashSet<uint>()));
    }

    [Fact]
    public void CountOwnershipStatesDistinguishesMissingFromUnverifiedCandidates()
    {
        var knownMissing = Item(1, "Known", ShoppingCategory.Outfit, 0, 10);
        var unverified = Item(2, "Unknown", ShoppingCategory.Outfit, 0, 20) with { OwnershipKnown = false };
        var owned = Item(3, "Owned", ShoppingCategory.Outfit, 0, 30, locallyOwned: true);
        var otherPage = Item(4, "Weapon", ShoppingCategory.Weapon, 0, 40);
        var completed = Item(5, "Completed", ShoppingCategory.Outfit, 0, 50);
        var secondKnownMissing = Item(6, "Also Known", ShoppingCategory.Outfit, 0, 60);

        var counts = ShoppingProjection.CountOwnershipStates(
            [knownMissing, unverified, owned, otherPage, completed, secondKnownMissing],
            ShopperPage.Outfits,
            new HashSet<uint> { 5 });

        Assert.Equal(new ShoppingOwnershipCounts(2, 1), counts);
    }

    [Fact]
    public void CountOwnershipStatesRejectsMissingInputs()
    {
        var completed = new HashSet<uint>();

        Assert.Equal("items", Assert.Throws<ArgumentNullException>(() =>
            ShoppingProjection.CountOwnershipStates(null!, ShopperPage.Outfits, completed)).ParamName);
        Assert.Equal("completedItemIds", Assert.Throws<ArgumentNullException>(() =>
            ShoppingProjection.CountOwnershipStates([], ShopperPage.Outfits, null!)).ParamName);
    }

    [Theory]
    [InlineData(false, false, false, (int)ShoppingEmptyState.NotLoaded)]
    [InlineData(false, true, true, (int)ShoppingEmptyState.NotLoaded)]
    [InlineData(true, false, true, (int)ShoppingEmptyState.Filtered)]
    [InlineData(true, false, false, (int)ShoppingEmptyState.OwnershipUnavailable)]
    [InlineData(true, true, false, (int)ShoppingEmptyState.Complete)]
    public void ResolveEmptyStatePreservesUnavailableAndFilteredMeaning(
        bool loaded,
        bool canInferMissing,
        bool hasFilter,
        int expected)
    {
        Assert.Equal(
            (ShoppingEmptyState)expected,
            ShoppingProjection.ResolveEmptyState(loaded, canInferMissing, hasFilter));
    }

    [Fact]
    public void BuildVisibleRejectsMissingInputs()
    {
        var items = new[] { Item(1, "Alpha", ShoppingCategory.Outfit, 0, 10) };
        var completed = new HashSet<uint>();

        Assert.Equal("items", Assert.Throws<ArgumentNullException>(() =>
            ShoppingProjection.BuildVisible(
                null!, ShopperPage.Outfits, string.Empty, false, false, completed, 0, false)).ParamName);
        Assert.Equal("completedItemIds", Assert.Throws<ArgumentNullException>(() =>
            ShoppingProjection.BuildVisible(
                items, ShopperPage.Outfits, string.Empty, false, false, null!, 0, false)).ParamName);
    }

    [Fact]
    public void BuildVisibleAppliesEveryOrderingKey()
    {
        var items = new[]
        {
            Item(1, "Zulu", ShoppingCategory.Outfit, priority: 1, price: 10),
            Item(2, "Zulu", ShoppingCategory.Outfit, priority: 0, price: null),
            Item(3, "Zulu", ShoppingCategory.Outfit, priority: 0, price: 20),
            Item(4, "Beta", ShoppingCategory.Outfit, priority: 0, price: 10),
            Item(5, "Alpha", ShoppingCategory.Outfit, priority: 0, price: 10),
        };

        var result = ShoppingProjection.BuildVisible(
            items, ShopperPage.Outfits, string.Empty, false, false, new HashSet<uint>(), 0, false);

        Assert.Equal([5U, 4U, 3U, 2U, 1U], result.Select(item => item.ItemId));
    }

    [Fact]
    public void WhitespaceFilterDoesNotHideItems()
    {
        var result = ShoppingProjection.BuildVisible(
            [Item(1, "Alpha", ShoppingCategory.Outfit, 0, 10)],
            ShopperPage.Outfits,
            "\t",
            false,
            false,
            new HashSet<uint>(),
            0,
            false);

        Assert.Single(result);
    }

    [Fact]
    public void NonMatchingFilterHidesNonCurrentItems()
    {
        var result = ShoppingProjection.BuildVisible(
            [Item(1, "Alpha", ShoppingCategory.Outfit, 0, 10)],
            ShopperPage.Outfits,
            "does-not-match",
            false,
            false,
            new HashSet<uint>(),
            0,
            false);

        Assert.Empty(result);
    }

    [Fact]
    public void LowerPriceSortsBeforeAlphabeticalName()
    {
        var result = ShoppingProjection.BuildVisible(
            [
                Item(1, "Alpha", ShoppingCategory.Outfit, 0, 100),
                Item(2, "Zulu", ShoppingCategory.Outfit, 0, 50),
            ],
            ShopperPage.Outfits,
            string.Empty,
            false,
            false,
            new HashSet<uint>(),
            0,
            false);

        Assert.Equal([2U, 1U], result.Select(item => item.ItemId));
    }

    [Fact]
    public void EveryShoppingPageMapsOnlyToItsCategory()
    {
        var pairs = new[]
        {
            (ShopperPage.Outfits, ShoppingCategory.Outfit),
            (ShopperPage.Weapons, ShoppingCategory.Weapon),
            (ShopperPage.Minions, ShoppingCategory.Minion),
            (ShopperPage.Mounts, ShoppingCategory.Mount),
            (ShopperPage.Orchestrions, ShoppingCategory.Orchestrion),
            (ShopperPage.Hairstyles, ShoppingCategory.Hairstyle),
            (ShopperPage.Facewear, ShoppingCategory.Facewear),
            (ShopperPage.Fashion, ShoppingCategory.Fashion),
        };

        foreach (var (page, category) in pairs)
        {
            Assert.True(ShoppingProjection.IncludesPage(page, category));
            Assert.All(Enum.GetValues<ShoppingCategory>().Where(candidate => candidate != category),
                candidate => Assert.False(ShoppingProjection.IncludesPage(page, candidate)));
            Assert.True(ShoppingProjection.IncludesPage(ShopperPage.AllMissing, category));
            Assert.False(ShoppingProjection.IncludesPage(ShopperPage.Settings, category));
        }
    }

    [Fact]
    public void BuildVisibleFiltersAndOrdersWithoutMutatingInput()
    {
        var items = new[]
        {
            Item(3, "Gamma", ShoppingCategory.Outfit, priority: 2, price: 300),
            Item(1, "Alpha", ShoppingCategory.Outfit, priority: 1, price: 200),
            Item(2, "Beta", ShoppingCategory.Weapon, priority: 1, price: 100),
            Item(4, "Delta", ShoppingCategory.Outfit, priority: 1, price: null, locallyOwned: true),
        };

        var result = ShoppingProjection.BuildVisible(
            items,
            ShopperPage.Outfits,
            string.Empty,
            hideLocallyOwned: true,
            hideCompleted: true,
            new HashSet<uint> { 3 },
            currentItemId: 0,
            allowHighQuality: false);

        Assert.Equal([1U], result.Select(item => item.ItemId));
        Assert.Equal([3U, 1U, 2U, 4U], items.Select(item => item.ItemId));
    }

    [Fact]
    public void CurrentItemRemainsVisibleThroughTextFilter()
    {
        var result = ShoppingProjection.BuildVisible(
            [Item(1, "Alpha", ShoppingCategory.Outfit, 0, 10)],
            ShopperPage.Outfits,
            "does-not-match",
            hideLocallyOwned: false,
            hideCompleted: false,
            new HashSet<uint>(),
            currentItemId: 1,
            allowHighQuality: false);

        Assert.Single(result);
    }

    [Fact]
    public void PreferredPriceUsesNormalQualityBeforeOptionalHighQuality()
    {
        var hqOnly = Item(1, "Alpha", ShoppingCategory.Outfit, 0, null, highQualityPrice: 50);
        var both = Item(2, "Beta", ShoppingCategory.Outfit, 0, 60, highQualityPrice: 40);

        Assert.Null(ShoppingProjection.PreferredPrice(hqOnly, allowHighQuality: false));
        Assert.Equal(50U, ShoppingProjection.PreferredPrice(hqOnly, allowHighQuality: true)?.PricePerUnit);
        Assert.Equal(60U, ShoppingProjection.PreferredPrice(both, allowHighQuality: true)?.PricePerUnit);
    }

    [Fact]
    public void GeneratedProjectionsAreDeterministicAndRespectAllFilters()
    {
        var random = new Random(20260901);
        for (var iteration = 0; iteration < 128; iteration++)
        {
            var items = Enumerable.Range(1, 128)
                .Select(index => Item(
                    (uint)index,
                    $"Item {index:D3}",
                    (ShoppingCategory)random.Next(Enum.GetValues<ShoppingCategory>().Length),
                    random.Next(8),
                    random.Next(3) == 0 ? null : (uint)random.Next(1, 100_000),
                    locallyOwned: random.Next(4) == 0))
                .ToArray();
            var completed = items
                .Where(_ => random.Next(5) == 0)
                .Select(item => item.ItemId)
                .ToHashSet();
            var page = (ShopperPage)random.Next(Enum.GetValues<ShopperPage>().Length - 1);

            var first = ShoppingProjection.BuildVisible(
                items, page, "Item", true, true, completed, 0, false);
            var second = ShoppingProjection.BuildVisible(
                items, page, "Item", true, true, completed, 0, false);

            Assert.Equal(first.Select(item => item.ItemId), second.Select(item => item.ItemId));
            Assert.All(first, item =>
            {
                Assert.True(ShoppingProjection.IncludesPage(page, item.Category));
                Assert.False(item.IsLocallyOwned);
                Assert.DoesNotContain(item.ItemId, completed);
            });
        }
    }

    private static ShoppingItem Item(
        uint id,
        string name,
        ShoppingCategory category,
        int priority,
        uint? price,
        uint? highQualityPrice = null,
        bool locallyOwned = false)
        => new(
            id,
            name,
            id,
            $"Collection {id}",
            null,
            string.Empty,
            "Fixture",
            0,
            true,
            locallyOwned,
            price is null && highQualityPrice is null
                ? null
                : new MarketPriceQuote(
                    price is null ? null : new MarketPrice(id, price.Value, 1, "Fixture", false),
                    highQualityPrice is null
                        ? null
                        : new MarketPrice(id, highQualityPrice.Value, 1, "Fixture", true)),
            category,
            priority,
            1);
}
