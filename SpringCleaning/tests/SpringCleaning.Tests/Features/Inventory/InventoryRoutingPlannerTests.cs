namespace SpringCleaning.Tests;

public sealed class InventoryRoutingPlannerTests
{
    [Fact]
    public void NonArmoireOutfitPieceGoesToDresser() =>
        Assert.True(InventoryRoutingPlanner.ShouldOfferDresser(true, true, false, false, false));

    [Fact]
    public void ArmoireItemIsNotDivertedBeforeArmoireStorage() =>
        Assert.False(InventoryRoutingPlanner.ShouldOfferDresser(true, true, false, true, false));

    [Fact]
    public void ArmoireDuplicateCanUseDresserFallback() =>
        Assert.True(InventoryRoutingPlanner.ShouldOfferDresser(true, true, false, true, true));

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void UnsafeUngroupedOrStoredItemsAreSkipped(
        bool isSafe,
        bool belongsToOutfit,
        bool isStoredInDresser) =>
        Assert.False(InventoryRoutingPlanner.ShouldOfferDresser(
            isSafe,
            belongsToOutfit,
            isStoredInDresser,
            false,
            false));
}
