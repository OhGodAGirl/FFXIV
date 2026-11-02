namespace CollectionShopper;

internal readonly record struct ShoppingOfferings(int RequestId, IReadOnlyList<uint> ItemIds);
