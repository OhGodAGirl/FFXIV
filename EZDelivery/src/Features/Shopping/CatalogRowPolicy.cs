namespace EZDelivery.Features.Shopping;

/// <summary>Admission and deterministic ordering rules for catalog rows.</summary>
public static class CatalogRowPolicy
{
    /// <summary>Rejects invalid native row references before item-sheet lookup.</summary>
    public static bool CanReadItem(uint company, int tier, int category, uint item, uint cost) =>
        company is >= 1 and <= 3 && tier is >= 1 and <= 3 && category is >= 1 and <= 4 && item != 0 && cost != 0;

    /// <summary>Requires a supported category, byte-sized required rank and positive stack size.</summary>
    public static bool CanPublish(ShopCategory? classification, uint rank, uint stackSize) =>
        classification is not null && rank <= byte.MaxValue && stackSize != 0;

    /// <summary>Orders offers by company, rank tier, native category, sort key and sheet subrow.</summary>
    public static ShopOffer[] Sort(IEnumerable<ShopOffer> offers) =>
        offers.OrderBy(x => x.Company).ThenBy(x => x.Tier).ThenBy(x => x.NativeCategory)
            .ThenBy(x => x.SortKey).ThenBy(x => x.SheetSubrowId).ToArray();
}
