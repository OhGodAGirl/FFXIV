namespace CollectionShopper;

internal readonly record struct ItemOwnership(bool Known, bool SuppressPurchase, bool PermanentlyUnlocked, string Reason);
