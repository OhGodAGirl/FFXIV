using EZDelivery.Features.Shopping;

namespace EZDelivery.Tests.Shopping;

public sealed class CatalogRowPolicyTests
{
    [Fact]
    public void OnlySupportedCompanyTierAndCategoryCombinationsCanReadItem()
    {
        // Arrange
        uint[] companies = [0, 1, 2, 3, 4, uint.MaxValue];
        int[] tiers = [int.MinValue, -1, 0, 1, 2, 3, 4, int.MaxValue];
        int[] categories = [int.MinValue, -1, 0, 1, 2, 3, 4, 5, int.MaxValue];

        foreach (var company in companies)
        foreach (var tier in tiers)
        foreach (var category in categories)
        {
            // Act
            var accepted = CatalogRowPolicy.CanReadItem(company, tier, category, 1, 1);

            // Assert
            Assert.Equal(new uint[] { 1, 2, 3 }.Contains(company) &&
                new[] { 1, 2, 3 }.Contains(tier) && new[] { 1, 2, 3, 4 }.Contains(category), accepted);
        }
    }

    [Theory]
    [InlineData(0u, 1u, false)]
    [InlineData(1u, 0u, false)]
    [InlineData(0u, 0u, false)]
    [InlineData(1u, 1u, true)]
    [InlineData(uint.MaxValue, uint.MaxValue, true)]
    public void ItemAndCostMustBeNonzero(uint item, uint cost, bool expected)
    {
        // Arrange
        const uint company = 1;

        // Act
        var accepted = CatalogRowPolicy.CanReadItem(company, 1, 1, item, cost);

        // Assert
        Assert.Equal(expected, accepted);
    }

    [Fact]
    public void PublishedRowsNeedClassificationByteSizedRankAndNonzeroStack()
    {
        // Arrange
        ShopCategory?[] categories = [null, ShopCategory.Weapons, ShopCategory.Armor, ShopCategory.Barding, ShopCategory.Furniture];
        uint[] ranks = [0, 1, 254, 255, 256, uint.MaxValue];
        uint[] stacks = [0, 1, uint.MaxValue];

        foreach (var category in categories)
        foreach (var rank in ranks)
        foreach (var stack in stacks)
        {
            // Act
            var accepted = CatalogRowPolicy.CanPublish(category, rank, stack);

            // Assert
            Assert.Equal(category.HasValue && rank < 256 && stack > 0, accepted);
        }
    }

    [Fact]
    public void SortUsesEveryNativeOrderingKeyAndPreservesExactTies()
    {
        // Arrange: each later row differs first at a successively higher-priority key.
        var first = new ShopOffer("first", 1, 1, "Z", ShopCategory.Weapons, 1, 1, 0, 0, 1, 0, 1, 0, 0);
        var tie = first with { OfferKey = "tie", ItemId = 2, Name = "A" };
        var subrow = first with { OfferKey = "subrow", SheetSubrowId = 1 };
        var sortKey = first with { OfferKey = "sort", SortKey = 1 };
        var category = first with { OfferKey = "category", NativeCategory = 1 };
        var tier = first with { OfferKey = "tier", Tier = 1 };
        var company = first with { OfferKey = "company", Company = 2 };
        ShopOffer[] input = [company, tier, category, sortKey, subrow, first, tie];

        // Act
        var sorted = CatalogRowPolicy.Sort(input);

        // Assert
        Assert.Equal(new[] { first, tie, subrow, sortKey, category, tier, company }, sorted);
        Assert.Equal(company, input[0]);
        Assert.NotSame(input, sorted);
        Assert.Empty(CatalogRowPolicy.Sort([]));
    }
}
