namespace SpringCleaning;

internal static class InventoryRoutingPlanner
{
    public static bool ShouldOfferDresser(
        bool isSafe,
        bool belongsToOutfit,
        bool isStoredInDresser,
        bool isArmoireEligible,
        bool isStoredInArmoire) =>
        isSafe
        && belongsToOutfit
        && !isStoredInDresser
        && (!isArmoireEligible || isStoredInArmoire);
}
