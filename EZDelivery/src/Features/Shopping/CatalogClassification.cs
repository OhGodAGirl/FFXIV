namespace EZDelivery.Features.Shopping;

/// <summary>Classifies Quartermaster rows and freezes validated offer lists.</summary>
public static class CatalogClassification
{
    // IDs are installed game-sheet identities, never localized names.
    /// <summary>Selects a supported collection category from item-sheet facts.</summary>
    public static ShopCategory? Classify(bool mainHand, bool otherEquipment, uint uiCategory,
        uint actionId, uint actionData, bool buddyEquipExists)
    {
        if (mainHand) return ShopCategory.Weapons;
        if (otherEquipment) return ShopCategory.Armor;
        if (actionId == 1013 && actionData != 0 && buddyEquipExists) return ShopCategory.Barding;
        return uiCategory is 57 or 65 or 66 or 67 or 68 or 69 or 70 or 71 or 72 or 73 or 74 or 75 or 76 or 77 or 78 or 79 or 80 or 95
            ? ShopCategory.Furniture : null;
    }

    /// <summary>Validates and snapshots offers, retaining only the first offer per item ID.</summary>
    public static ShopOffer[] Snapshot(IEnumerable<ShopOffer> displayed)
    {
        var seen = new HashSet<uint>();
        var queue = new List<ShopOffer>();
        foreach (var offer in displayed)
        {
            if (offer.ItemId == 0 || offer.UnitCost == 0 || offer.Company is < 1 or > 3 ||
                offer.Tier is < 0 or > 2 || offer.NativeCategory is < 0 or > 3 || string.IsNullOrWhiteSpace(offer.OfferKey))
                throw new ArgumentException("The displayed catalog contains an invalid offer.");
            if (!seen.Add(offer.ItemId)) continue;
            if (queue.Count == 2048) throw new ArgumentException("The displayed catalog exceeds the purchase limit.");
            queue.Add(offer);
        }
        return queue.ToArray();
    }

    /// <summary>Sums unit seal prices with checked arithmetic.</summary>
    public static ulong Total(IEnumerable<ShopOffer> offers)
    {
        ulong total = 0;
        foreach (var offer in offers) total = checked(total + offer.UnitCost);
        return total;
    }
}
