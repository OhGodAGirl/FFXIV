using EZDelivery.Features.Shopping;

namespace EZDelivery.Tests.Shopping;

public sealed class CatalogClassificationTests
{
    private static ShopOffer Offer(uint id) => new($"offer:{id}", id, 1, "Item", ShopCategory.Weapons, 1, 1, 0, 0, 1, 0, 1, 0, 0);

    [Theory]
    [InlineData("item")]
    [InlineData("price")]
    [InlineData("company-low")]
    [InlineData("company-high")]
    [InlineData("tier-low")]
    [InlineData("tier-high")]
    [InlineData("category-low")]
    [InlineData("category-high")]
    [InlineData("key")]
    public void InvalidCatalogIdentityCannotEnterPurchaseQueue(string invalid)
    {
        var offer = Offer(1);
        offer = invalid switch
        {
            "item" => offer with { ItemId = 0 },
            "price" => offer with { UnitCost = 0 },
            "company-low" => offer with { Company = 0 },
            "company-high" => offer with { Company = 4 },
            "tier-low" => offer with { Tier = -1 },
            "tier-high" => offer with { Tier = 3 },
            "category-low" => offer with { NativeCategory = -1 },
            "category-high" => offer with { NativeCategory = 4 },
            _ => offer with { OfferKey = " " },
        };
        Assert.Throws<ArgumentException>(() => CatalogClassification.Snapshot([offer]));
    }

    [Fact]
    public void MaximumQueueIsStableDeduplicatedAndTotalsWithoutUintOverflow()
    {
        var expected = Enumerable.Range(1, 2048).Select(id => Offer((uint)id) with { UnitCost = uint.MaxValue }).ToArray();
        var displayed = expected.SelectMany(offer => new[] { offer, offer with { OfferKey = offer.OfferKey + ":duplicate", UnitCost = 1 } });
        var queue = CatalogClassification.Snapshot(displayed);
        Assert.Equal(expected, queue);
        Assert.Equal(2048UL * uint.MaxValue, CatalogClassification.Total(queue));
        Assert.Equal(0UL, CatalogClassification.Total([]));
    }

    [Fact]
    public void GeneratedCatalogPermutationsKeepFirstDisplayedOfferPerExactItem()
    {
        var random = new Random(2419);
        for (var sample = 0; sample < 200; sample++)
        {
            var displayed = Enumerable.Range(0, random.Next(1, 100)).Select(index => Offer((uint)random.Next(1, 25)) with { OfferKey = $"row:{index}" }).ToArray();
            var expected = displayed.GroupBy(offer => offer.ItemId).Select(group => group.First()).ToArray();
            Assert.Equal(expected, CatalogClassification.Snapshot(displayed));
        }
    }

    [Fact]
    public void FurnitureUsesOnlyDeclaredGameCategoryIds()
    {
        uint[] furniture = [57, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 95];
        for (uint category = 0; category < 128; category++)
            Assert.Equal(furniture.Contains(category) ? ShopCategory.Furniture : (ShopCategory?)null,
                CatalogClassification.Classify(false, false, category, 0, 0, false));
    }
}
