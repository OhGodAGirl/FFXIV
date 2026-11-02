namespace EZDelivery.Features.Interface;

/// <summary>Player-facing categories; deliberately independent of native shop tabs.</summary>
public enum ShopCategory
{
    /// <summary>Missing weapon appearances.</summary>
    Weapons,
    /// <summary>Missing armor appearances.</summary>
    Armor,
    /// <summary>Missing chocobo barding unlocks.</summary>
    Barding,
    /// <summary>Furnishings absent from tracked storage; placed furnishings are not counted.</summary>
    Furniture,
}
/// <summary>Player-selectable shape for attached tabs.</summary>
public enum ShopTabStyle
{
    /// <summary>Tabs recessed into a bordered socket.</summary>
    InsetSockets,
    /// <summary>Flat tabs aligned to the window edge.</summary>
    FlushIndex,
    /// <summary>Tabs with a folded ribbon edge.</summary>
    RibbonTabs,
    /// <summary>Tabs with a rounded outer edge.</summary>
    RoundTabs,
    /// <summary>Tabs with clipped outer corners.</summary>
    BeveledIndex,
}

/// <summary>Screen rectangle already in ImGui coordinates. Do not apply native scale again.</summary>
public readonly record struct UiRect(float X, float Y, float Width, float Height)
{
    /// <summary>Right screen coordinate derived from origin and width.</summary>
    public float Right => X + Width;
    /// <summary>Bottom screen coordinate derived from origin and height.</summary>
    public float Bottom => Y + Height;
    /// <summary>Requires finite coordinates and positive, finite extents.</summary>
    public bool IsValid => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Width) &&
                           float.IsFinite(Height) && Width > 0 && Height > 0 &&
                           float.IsFinite(Right) && float.IsFinite(Bottom);
    /// <summary>Tests strict overlap with another screen rectangle.</summary>
    public bool Intersects(UiRect other) => X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;
}

/// <summary>Native viewport-local pixel geometry; size already includes native scale.
/// Renderer adds the ImGui viewport origin exactly once, without scaling the bounds.</summary>
public sealed record NativeAnchor(UiRect Bounds, bool Ready);
/// <summary>Prepared delivery controls and confirmed count shown by the renderer.</summary>
public sealed record DeliveryView(bool Running, bool CanStart, string Status, int ConfirmedCount);
/// <summary>Prepared item row with purchase eligibility and a reason when buying is unavailable.</summary>
public sealed record ShopRow(string OfferKey, uint ItemId, uint IconId, string Name, ShopCategory Category,
    ulong SealCost, bool CanBuy, string DisabledReason);
/// <summary>Prepared collection status, missing offers and purchase progress.</summary>
public sealed record ShoppingView(bool Running, bool CollectionUsable, string CollectionStatus,
    IReadOnlyList<ShopRow> Rows, ulong SealBalance, string Status, int ConfirmedCount, string? CurrentOfferKey = null);

/// <summary>Prepared outside Draw. Rows must only contain definitely missing offers.</summary>
public sealed record UiState(long Generation, bool LoggedIn, NativeAnchor? DeliveryAnchor, NativeAnchor? ShopAnchor,
    bool ExpertDeliverySelected, DeliveryView Delivery, ShoppingView Shopping, bool AnyOperationRunning,
    bool ReduceMotion, string DiagnosticsReport)
{
    /// <summary>Prepared shape preference used by both attached tab surfaces.</summary>
    public ShopTabStyle ShopTabStyle { get; init; } = ShopTabStyle.InsetSockets;
    /// <summary>Initial state with no character, native windows or collection data.</summary>
    public static UiState Empty { get; } = new(0, false, null, null, false,
        new(false, false, "Open the Personnel Officer's Expert Delivery tab.", 0),
        new(false, false, "Collection data unavailable", Array.Empty<ShopRow>(), 0, "", 0), false, false, "No diagnostics prepared.");
}

/// <summary>Presentation-only projection, recomputed outside Draw when state/search changes.</summary>
public sealed record BrowserView(IReadOnlyList<ShopRow> Rows, IReadOnlyList<string> BuyAllKeys,
    ulong? TotalCost, int MissingCount, bool SearchActive);
