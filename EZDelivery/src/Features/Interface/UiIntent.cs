namespace EZDelivery.Features.Interface;

/// <summary>All consequential intents must be revalidated against Generation on the framework thread.</summary>
public abstract record UiIntent(long Generation);
/// <summary>Requests delivery start after current-generation validation.</summary>
public sealed record StartDeliveryIntent(long Generation) : UiIntent(Generation);
/// <summary>Requests that no further delivery actions start.</summary>
public sealed record StopDeliveryIntent(long Generation) : UiIntent(Generation);
/// <summary>Requests one catalog offer identified by its stable offer key.</summary>
public sealed record BuyOneIntent(long Generation, string OfferKey) : UiIntent(Generation);
/// <summary>Requests one copy of each offer in the captured displayed list.</summary>
public sealed record BuyAllIntent(long Generation, IReadOnlyList<string> OfferKeys) : UiIntent(Generation);
/// <summary>Requests that no further purchases start.</summary>
public sealed record StopPurchasesIntent(long Generation) : UiIntent(Generation);
/// <summary>Selects a collection category for the item browser.</summary>
public sealed record SelectCategoryIntent(long Generation, ShopCategory Category) : UiIntent(Generation);
/// <summary>Closes the plugin item browser.</summary>
public sealed record CloseBrowserIntent(long Generation) : UiIntent(Generation);
/// <summary>Changes the browser search query without changing an active purchase run.</summary>
public sealed record SearchIntent(long Generation, string Query) : UiIntent(Generation);
/// <summary>Changes the persisted reduced-motion preference.</summary>
public sealed record ReduceMotionIntent(long Generation, bool Enabled) : UiIntent(Generation);
/// <summary>Changes the persisted shape shared by delivery and shopping tabs.</summary>
public sealed record ShopTabStyleIntent(long Generation, ShopTabStyle Style) : UiIntent(Generation);
/// <summary>Copies an already prepared diagnostic report without native reads.</summary>
public sealed record CopyDiagnosticsIntent(long Generation, string PreparedReport) : UiIntent(Generation);
