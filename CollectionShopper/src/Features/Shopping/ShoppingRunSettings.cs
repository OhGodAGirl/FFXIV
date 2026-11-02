namespace CollectionShopper;

internal readonly record struct ShoppingRunSettings(
    int MaximumPricePerItem,
    bool UnlimitedPricePerItem,
    int MaximumTotalSpend,
    bool UnlimitedTotalSpend,
    bool EnableHighQualityPurchases,
    int ActionDelayMilliseconds);
