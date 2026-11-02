using Dalamud.Game.Network.Structures;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace CollectionShopper;

/// <summary>Owns native Market Board access and the controller's event subscriptions.</summary>
internal sealed class ShoppingMarketBoardAdapter : IShoppingMarketBoard
{
    private readonly IFramework framework;
    private readonly IMarketBoard marketBoard;
    private readonly IGameGui gameGui;
    private readonly ShoppingEventQueue events = new();
    private bool disposed;
    private readonly Func<bool> isDisposed;
    private readonly Action deliverOverflow;
    private readonly Action<ShoppingOfferings> deliverOfferings;
    private readonly Action<ShoppingPurchase> deliverPurchase;
    private readonly Action deliverUpdate;
    public IReadOnlyList<DiagnosticPredicate> LastSubmissionChecks { get; private set; } = Array.Empty<DiagnosticPredicate>();
    public IReadOnlyList<DiagnosticPredicate> LastListingRequestChecks { get; private set; } = Array.Empty<DiagnosticPredicate>();

    internal ShoppingMarketBoardAdapter(IFramework framework, IMarketBoard marketBoard, IGameGui gameGui)
    {
        this.framework = framework;
        this.marketBoard = marketBoard;
        this.gameGui = gameGui;
        isDisposed = () => disposed;
        deliverOverflow = () => EventStreamFaulted?.Invoke();
        deliverOfferings = offerings => OfferingsReceived?.Invoke(offerings);
        deliverPurchase = purchase => ItemPurchased?.Invoke(purchase);
        deliverUpdate = () => Updated?.Invoke();
        framework.Update += OnFrameworkUpdate;
        marketBoard.OfferingsReceived += OnOfferingsReceived;
        marketBoard.ItemPurchased += OnItemPurchased;
    }

    public event Action? Updated;
    public event Action<ShoppingOfferings>? OfferingsReceived;
    public event Action<ShoppingPurchase>? ItemPurchased;
    public event Action? EventStreamFaulted;

    public bool IsOpen => !disposed && MarketBoardContext.IsBrowsing(name =>
    {
        var addon = gameGui.GetAddonByName(name);
        return !addon.IsNull && addon.IsVisible;
    });

    public unsafe bool IsAvailable => !disposed && InfoProxyItemSearch.Instance() != null;

    public unsafe bool RequestListings(uint itemId, out int requestId)
    {
        requestId = -1;
        var proxy = InfoProxyItemSearch.Instance();
        var open = !disposed && IsOpen;
        var checks = new List<DiagnosticPredicate>
        {
            new("Requested listing item", null, "Observed item ID", itemId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("Listing adapter active", !disposed, "True", (!disposed).ToString()),
            new("Listing browsing context open", open, "True", open.ToString()),
            new("Listing native proxy available", proxy != null, "True", (proxy != null).ToString())
        };
        LastListingRequestChecks = checks.ToArray();
        if (disposed || !open || proxy == null)
            return false;

        // Native request flags are diagnostic observations, not proof of readiness or completion.
        checks.Add(new("Native listing state before request", null, "Observed state", ListingState(proxy)));
        proxy->SearchItemId = itemId;
        var accepted = proxy->RequestData();
        checks.Add(new("Native listing state after request", null, "Observed state", ListingState(proxy)));
        LastListingRequestChecks = checks.ToArray();
        if (!accepted)
            return false;
        requestId = proxy->CurrentRequestId;
        return true;
    }

    private static unsafe string ListingState(InfoProxyItemSearch* proxy)
        => $"Item {proxy->SearchItemId}; request {proxy->CurrentRequestId}; next {proxy->NextRequestId}; waiting {proxy->WaitingForListings}; count {proxy->ListingCount}";

    public unsafe bool TryReadListings(out uint itemId, out IReadOnlyList<PurchaseListingCandidate> listings)
    {
        itemId = 0;
        listings = Array.Empty<PurchaseListingCandidate>();
        var proxy = InfoProxyItemSearch.Instance();
        if (disposed || !IsOpen || proxy == null)
            return false;
        itemId = proxy->SearchItemId;
        var count = (int)Math.Min(proxy->ListingCount, (uint)proxy->Listings.Length);
        var snapshot = new PurchaseListingCandidate[count];
        for (var index = 0; index < count; index++)
            snapshot[index] = Candidate(index, proxy->Listings[index]);
        listings = snapshot;
        return true;
    }

    public unsafe PurchaseListingCandidate? ReadListing(int index)
    {
        var proxy = InfoProxyItemSearch.Instance();
        if (disposed || !IsOpen || proxy == null || index < 0
            || (uint)index >= proxy->ListingCount || index >= proxy->Listings.Length)
            return null;
        return Candidate(index, proxy->Listings[index]);
    }

    public unsafe bool SubmitPurchase(PurchaseListingCandidate expected)
    {
        var proxy = InfoProxyItemSearch.Instance();
        var open = !disposed && IsOpen;
        var context = new ShoppingSubmissionContext(!disposed, open, proxy != null,
            proxy == null ? 0 : proxy->SearchItemId,
            proxy == null ? 0 : proxy->ListingCount,
            proxy == null ? 0 : proxy->Listings.Length);
        MarketBoardListing verified = default;
        IReadOnlyList<DiagnosticPredicate> checks = Array.Empty<DiagnosticPredicate>();
        try
        {
            return ShoppingSubmissionPolicy.Submit(context, expected,
                index =>
                {
                    verified = proxy->Listings[index];
                    return Candidate(index, verified);
                },
                () =>
                {
                    // Keep the complete native row, not a reconstructed decision candidate.
                    var snapshot = verified;
                    return proxy->SetLastPurchasedItem(&snapshot);
                },
                () => proxy->SendPurchaseRequestPacket(), out checks);
        }
        finally
        {
            LastSubmissionChecks = checks;
        }
    }
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        events.Dispose();
        marketBoard.ItemPurchased -= OnItemPurchased;
        marketBoard.OfferingsReceived -= OnOfferingsReceived;
        framework.Update -= OnFrameworkUpdate;
    }

    private static PurchaseListingCandidate Candidate(int index, MarketBoardListing listing)
        => new(index, listing.ItemId, listing.ListingId, listing.UnitPrice, listing.TotalTax,
            listing.Quantity, listing.IsHqItem, listing.IsMannequin);

    private void OnFrameworkUpdate(IFramework _)
        => ShoppingEventDelivery.Dispatch(events, isDisposed,
            deliverOverflow, deliverOfferings, deliverPurchase, deliverUpdate);

    private void OnOfferingsReceived(IMarketBoardCurrentOfferings offerings)
    {
        if (!disposed)
            events.Enqueue(new ShoppingOfferings(
                offerings.RequestId, offerings.ItemListings.Select(listing => listing.ItemId).ToArray()));
    }

    private void OnItemPurchased(IMarketBoardPurchase purchase)
    {
        if (disposed)
            return;
        var (itemId, _) = ItemUtil.GetBaseId(purchase.CatalogId);
        events.Enqueue(new ShoppingPurchase(purchase.CatalogId, itemId, purchase.ItemQuantity));
    }
}
