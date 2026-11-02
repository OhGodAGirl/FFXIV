namespace EZDelivery.Features.Shopping;

/// <summary>Coordinates catalog lookup, collection evidence and the purchase state machine.</summary>
public sealed partial class ShoppingFacade
{
    private readonly IShopCatalog catalog;
    private readonly IShoppingRuntime native;
    private readonly Func<DateTimeOffset> clock;
    private readonly PurchaseEngine engine = new();
    private readonly Func<ShopOffer, PurchaseKnowledge> knowledge;
    /// <summary>Current catalog snapshot.</summary>
    public IReadOnlyList<ShopOffer> Offers { get; private set; } = Array.Empty<ShopOffer>();
    /// <summary>Latest native shop observation.</summary>
    public PurchaseObservation Observation { get; private set; } = new(false, "Not ready", default, -1, -1, -1, 0, 0, 0, -1, false);
    /// <summary>Player-readable catalog or purchase status.</summary>
    public string Status => engine.Phase == PurchasePhase.Idle ? catalog.Status : engine.Status;
    /// <summary>Whether the purchase engine currently owns an operation.</summary>
    public bool IsRunning => engine.IsRunning;
    /// <summary>Number of verified purchases in the current run.</summary>
    public int ConfirmedCount => engine.ConfirmedCount;
    /// <summary>Latest native purchase dialog diagnostic.</summary>
    public string LastDialogCheck => native.LastDialogCheck;
    /// <summary>Native Seal Exchange bounds for the attached browser.</summary>
    public ShopAnchor? Anchor => native.Anchor;
    /// <summary>Item IDs suppressed by confirmed local purchases.</summary>
    public IReadOnlySet<uint> Suppressed => engine.Suppressed;
    /// <summary>Forwards confirmed purchase events to collection reporting.</summary>
    public event Action<ShopOffer>? Acquired;

    /// <summary>Connects catalog, native shop observations, collection evidence and time.</summary>
    public ShoppingFacade(IShopCatalog catalog, IShoppingRuntime native,
        Func<ShopOffer, PurchaseKnowledge> knowledge, Func<DateTimeOffset> clock)
    {
        this.catalog = catalog;
        this.native = native;
        this.knowledge = knowledge;
        this.clock = clock;
        engine.Acquired += offer => Acquired?.Invoke(offer);
    }
    /// <summary>Observes the shop and advances at most the next purchase action.</summary>
    public void Tick()
    {
        Observation = native.Observe(engine.Pending);
        engine.ClearCharacter(Observation.Identity.Character);
        var identity = Observation.Identity;
        Offers = identity.Character == 0 ? Array.Empty<ShopOffer>() : catalog.Offers
            .Where(x => x.Company == identity.Company && x.RequiredRank <= identity.Rank).ToArray();
        if (!engine.IsRunning || engine.Pending is not { } pending) return;
        var now = clock();
        var command = engine.Tick(Observation, knowledge(pending), now);
        if (command != null) engine.Dispatched(command, native.Dispatch(command), now);
    }

    /// <summary>Starts one purchase for an exact catalog offer key.</summary>
    public bool BuyOne(string offerKey) => BuyAll([offerKey]);
    /// <summary>Captures the selected offer keys as a purchase queue.</summary>
    public bool BuyAll(IEnumerable<string> displayedKeys)
    {
        if (engine.IsRunning) return false;
        var byKey = Offers.ToDictionary(x => x.OfferKey, StringComparer.Ordinal);
        var selected = new List<ShopOffer>();
        foreach (var key in displayedKeys.Take(2049))
        {
            if (!byKey.TryGetValue(key, out var offer) || knowledge(offer).Presence != PurchasePresence.Missing || engine.Suppressed.Contains(offer.ItemId)) return false;
            selected.Add(offer);
        }
        try { return engine.Start(selected, native.Observe(null).Identity); }
        catch (ArgumentException) { return false; }
    }
    /// <summary>Requests a stop without starting another purchase.</summary>
    public void Stop() => engine.Stop();
    /// <summary>Clears acquisition suppression for a verified delivery by the active character.</summary>
    public void ConfirmDelivery(ulong character, uint itemId) => engine.ConfirmDelivery(character, itemId);
    /// <summary>Clears local acquisition suppression while idle.</summary>
    public bool ResetPurchaseTracking() => engine.ResetPurchaseTracking();
}
