namespace EZDelivery.Features.Shopping;

/// <summary>Stable catalog offers and catalog preparation status.</summary>
public interface IShopCatalog
{
    /// <summary>Validated catalog offers available to shopping.</summary>
    IReadOnlyList<ShopOffer> Offers { get; }
    /// <summary>Catalog preparation or failure description.</summary>
    string Status { get; }
}

/// <summary>Shopping mechanism with presentation anchor and dialog diagnostics.</summary>
public interface IShoppingRuntime : IShoppingNative
{
    /// <summary>Most recent purchase confirmation correlation diagnostic.</summary>
    string LastDialogCheck { get; }
    /// <summary>Current native shop bounds, or null when unavailable.</summary>
    ShopAnchor? Anchor { get; }
}
