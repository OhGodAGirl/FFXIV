namespace EZDelivery.Features.Interface;

/// <summary>Functional copy lives here so wording can change without modifying transaction policy.</summary>
public static class UiCopy
{
    /// <summary>Explains stopping new actions while reconciling an action already sent.</summary>
    public const string Stop = "Stop new actions. Any action already sent is checked before finishing.";
    /// <summary>Explains the bulk purchase action.</summary>
    public const string BuyAll = "Buy one of each missing item shown, until the next purchase is unaffordable.";
    /// <summary>Explains the single-item seal purchase action or its disabled reason.</summary>
    public static string Buy(ShopRow row) => $"Buy one {row.Name} for {row.SealCost:N0} seals.";
}
