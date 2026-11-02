namespace EZDelivery.Features.Interface;

/// <summary>Whether an intent stops either operation or may run against the current generation.</summary>
public readonly record struct UiIntentRoute(bool StopDelivery, bool StopPurchases, bool ProcessCurrent);

/// <summary>Cancellation and generation rules shared by the pre-tick and post-observation passes.</summary>
public static class UiIntentRouting
{
    /// <summary>Identifies stop intents that must be applied before operation advancement.</summary>
    public static UiIntentRoute EvaluateBeforeTick(UiIntent intent)
        => new(intent is StopDeliveryIntent,
            intent is StopPurchasesIntent or CloseBrowserIntent or SelectCategoryIntent, false);

    /// <summary>Preserves stop intents across generation changes and validates other intents against current state.</summary>
    public static UiIntentRoute EvaluateAfterTick(UiIntent intent, long generation)
    {
        var current = intent.Generation == generation;
        var stopDelivery = intent is StopDeliveryIntent;
        var stopPurchases = intent is StopPurchasesIntent or CloseBrowserIntent ||
            current && intent is SelectCategoryIntent;
        return new(stopDelivery, stopPurchases, current && !stopDelivery);
    }
}
