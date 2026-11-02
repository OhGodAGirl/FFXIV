namespace SpringCleaning.Tests;

public sealed class ReviewPlannerTests
{
    [Fact]
    public void DefaultReviewKeepsCarriedDuplicatesAndExistingReviewAlerts()
    {
        var carried = Duplicate(10);
        var duplicate = Alert(20, DresserAlertKind.Duplicate, belongsToOutfit: true);
        var outfitDuplicate = Alert(
            24,
            DresserAlertKind.DuplicateInExistingOutfit,
            belongsToOutfit: true);
        var alreadyInArmoire = Alert(
            21,
            DresserAlertKind.ArmoireEligible,
            belongsToOutfit: false,
            isAlreadyInArmoire: true);
        var merelyEligible = Alert(
            22,
            DresserAlertKind.ArmoireEligible,
            belongsToOutfit: false);
        var outsideOutfit = Alert(23, DresserAlertKind.NotInOutfit, belongsToOutfit: false);

        var plan = ReviewPlanner.Plan(
            [carried],
            [duplicate, outfitDuplicate, alreadyInArmoire, merelyEligible, outsideOutfit],
            onlyItemsOutsideOutfits: false);

        Assert.Equal([carried], plan.InventoryDuplicates);
        Assert.Equal([duplicate, outfitDuplicate, alreadyInArmoire], plan.DresserEntries);
        Assert.Equal(4, plan.Count);
    }

    [Fact]
    public void OutsideOutfitFilterShowsOnlyUnattachedDresserItems()
    {
        var attached = Alert(20, DresserAlertKind.OutfitConvertible, belongsToOutfit: true);
        var unattached = Alert(21, DresserAlertKind.NotInOutfit, belongsToOutfit: false);
        var armoireEligibleUnattached = Alert(
            22,
            DresserAlertKind.ArmoireEligible,
            belongsToOutfit: false);

        var plan = ReviewPlanner.Plan(
            [Duplicate(10)],
            [attached, unattached, armoireEligibleUnattached],
            onlyItemsOutsideOutfits: true);

        Assert.Empty(plan.InventoryDuplicates);
        Assert.Equal([unattached, armoireEligibleUnattached], plan.DresserEntries);
        Assert.Equal(2, plan.Count);
    }

    [Fact]
    public void PlanRejectsNullCandidateCollections()
    {
        var inventoryError = Assert.Throws<ArgumentNullException>(() => ReviewPlanner.Plan(
            null!,
            Array.Empty<DresserAlertEntry>(),
            false));
        var dresserError = Assert.Throws<ArgumentNullException>(() => ReviewPlanner.Plan(
            Array.Empty<InventoryDuplicateItem>(),
            null!,
            false));

        Assert.Equal("inventoryDuplicates", inventoryError.ParamName);
        Assert.Equal("dresserEntries", dresserError.ParamName);
    }

    private static InventoryDuplicateItem Duplicate(uint itemId) => new(
        0,
        0,
        itemId,
        false,
        $"Duplicate {itemId}",
        0,
        new InventoryItemSafety(true, true, "Ready"),
        false,
        true,
        InventoryDuplicateDisposition.RemovalReview);

    private static DresserAlertEntry Alert(
        uint itemId,
        DresserAlertKind kind,
        bool belongsToOutfit,
        bool isAlreadyInArmoire = false) => new(
        -1,
        itemId,
        itemId,
        false,
        false,
        1,
        "Gear",
        $"Item {itemId}",
        0,
        kind,
        isAlreadyInArmoire,
        false,
        belongsToOutfit);
}
