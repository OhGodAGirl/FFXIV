namespace CollectionShopper;

internal readonly record struct ShoppingMarketEvent(ShoppingOfferings? Offerings, ShoppingPurchase? Purchase);
