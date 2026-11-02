namespace CollectionShopper;

internal sealed partial class ShoppingController : IDisposable
{
    private const int MinimumActionDelayMilliseconds = 500;
    private const int MaximumRememberedPurchaseRequests = 4_096;
    private const uint HighQualityItemIdOffset = 1_000_000;
    private static readonly TimeSpan ListingTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan PurchaseTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ListingQuietTime = TimeSpan.FromMilliseconds(250);

    private readonly IShoppingContext plugin;
    private readonly IShoppingMarketBoard marketBoard;
    private readonly Func<DateTime> utcNow;
    private ShoppingRunSettings settings;
    private uint runWorldId;
    private readonly Queue<ShoppingItem> queue = new();
    private readonly HashSet<uint> requestedItemIds = [];
    private readonly HashSet<ulong> requestedListingIds = [];
    private ShoppingItem? current;
    private DateTime nextActionAt;
    private DateTime deadline;
    private DateTime? ownershipDeadline;
    private DateTime lastListingAt;
    // Phase gates these values; each new search/submission replaces them before use.
    private int requestId;
    private int listingAttempts;
    private bool listingResponseSeen;
    private ulong pendingCost;
    private ulong pendingListingId;
    private bool pendingIsHighQuality;
    private ulong runContentId;
    private bool disposed;
    private readonly List<DiagnosticPredicate> diagnosticChecks = [];
    private DiagnosticFailure? lastFailure;
    internal DiagnosticOperationSnapshot CaptureDiagnostics() => new(
        Phase.ToString(), Status, PurchasedCount, diagnosticChecks.ToArray(), lastFailure);

    private bool Observe(string name, bool? passed, string expected = "true", string? observed = null)
    {
        // Names are fixed by this controller. Submission adds at most 16 checks,
        // and rejected listings add at most four rows; each new item resets them.
        var check = new DiagnosticPredicate(name, passed, expected, observed ?? passed?.ToString() ?? "Not observed");
        var index = diagnosticChecks.FindIndex(value => value.Name == name);
        if (index >= 0) diagnosticChecks[index] = check;
        else diagnosticChecks.Add(check);
        return passed == true;
    }

    private void RetainFailure(string reason)
        => lastFailure = new(new DateTimeOffset(DateTime.SpecifyKind(utcNow(), DateTimeKind.Utc)),
            Phase.ToString(), reason, PurchasedCount, diagnosticChecks.ToArray(), plugin.DiagnosticSources.Take(32).ToArray());

    private void Fail(string reason)
    {
        RetainFailure(reason);
        Stop(reason);
    }
    public string Status { get; private set; } = "Set spending options, then start at an open Market Board.";

    internal ShoppingController(IShoppingContext context, IShoppingMarketBoard marketBoard, Func<DateTime> utcNow)
    {
        plugin = context;
        this.marketBoard = marketBoard;
        this.utcNow = utcNow;
        marketBoard.Updated += OnFrameworkUpdate;
        marketBoard.OfferingsReceived += OnOfferingsReceived;
        marketBoard.ItemPurchased += OnItemPurchased;
        marketBoard.EventStreamFaulted += OnEventStreamFaulted;
    }

    public ShoppingPhase Phase { get; private set; }
    public bool IsRunning => Phase != ShoppingPhase.Idle;
    public int InitialCount { get; private set; }
    public int RemainingCount => queue.Count + (current is null ? 0 : 1);
    public int PurchasedCount { get; private set; }
    public int SkippedCount { get; private set; }
    public ulong Spent { get; private set; }
    public string CurrentItemName => current?.ItemName ?? string.Empty;
    public uint CurrentItemId => current?.ItemId ?? 0;

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        Stop("Automated purchasing stopped because the plugin is unloading.");
        marketBoard.ItemPurchased -= OnItemPurchased;
        marketBoard.EventStreamFaulted -= OnEventStreamFaulted;
        marketBoard.OfferingsReceived -= OnOfferingsReceived;
        marketBoard.Updated -= OnFrameworkUpdate;
        marketBoard.Dispose();
    }

    public bool Start(IEnumerable<ShoppingItem> candidates)
        => Start(candidates, false);

    public bool StartSingle(ShoppingItem item)
        => Start([item], true);

    public void Stop(string message = "Automated purchasing stopped.")
    {
        queue.Clear();
        ownershipDeadline = null;
        current = null;
        Phase = ShoppingPhase.Idle;
        Status = message;
        diagnosticChecks.Clear();
    }

    private bool Start(IEnumerable<ShoppingItem> candidates, bool singleItem)
    {
        if (disposed || IsRunning)
            return false;
        diagnosticChecks.Clear();
        if (!Observe("Market Board open", IsMarketBoardOpen()))
        {
            Status = "Open a Market Board before starting.";
            RetainFailure(Status);
            return false;
        }
        settings = plugin.Settings;
        if ((!settings.UnlimitedPricePerItem
                && settings.MaximumPricePerItem <= 0)
            || (!settings.UnlimitedTotalSpend
                && settings.MaximumTotalSpend <= 0))
        {
            Status = "Set each enabled spending cap above zero before starting.";
            Observe("Per-item cap enabled value", settings.UnlimitedPricePerItem || settings.MaximumPricePerItem > 0);
            Observe("Run cap enabled value", settings.UnlimitedTotalSpend || settings.MaximumTotalSpend > 0);
            RetainFailure(Status);
            return false;
        }

        queue.Clear();
        runContentId = plugin.CurrentContentId;
        runWorldId = plugin.CurrentWorldId;
        foreach (var item in PurchasePolicy.PrepareRun(
                     candidates.Where(item => plugin.IsStillMissing(item, runContentId)),
                     plugin.CompletedItemIds))
        {
            queue.Enqueue(item);
        }

        if (queue.Count == 0)
        {
            Status = "There are no visible missing items to buy.";
            Observe("Eligible plan has items", false);
            RetainFailure(Status);
            return false;
        }

        InitialCount = queue.Count;
        PurchasedCount = 0;
        SkippedCount = 0;
        Spent = 0;
        current = null;
        Phase = ShoppingPhase.WaitingToSearch;
        nextActionAt = utcNow();
        Status = singleItem
            ? $"Finding a listing to buy {queue.Peek().ItemName}..."
            : $"Automated purchasing started for {InitialCount:N0} items.";
        return true;
    }

    private void OnFrameworkUpdate()
    {
        if (disposed || !IsRunning)
            return;
        if (!Observe("Market Board open", IsMarketBoardOpen()))
        {
            Fail("Market Board closed; automated purchasing stopped.");
            return;
        }

        if (!Observe("Run identity unchanged", HasRunContext()))
        {
            Fail("Character or world changed; automated purchasing stopped.");
            return;
        }

        var now = utcNow();
        if (Phase == ShoppingPhase.WaitingForOwnership)
        {
            if (CheckOwnership(current!, now, "Ownership authorizes missing item"))
                SearchNext(now);
            return;
        }
        if (Phase == ShoppingPhase.WaitingToSearch)
        {
            if (now >= nextActionAt)
                SearchNext(now);
            return;
        }
        if (Phase == ShoppingPhase.WaitingForListings)
        {
            if (listingResponseSeen && now - lastListingAt >= ListingQuietTime)
                BuyCheapestCurrentListing(now);
            else if (now >= deadline)
            {
                Observe("Listings arrived before deadline", false, deadline.ToString("O", System.Globalization.CultureInfo.InvariantCulture), now.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                if (listingAttempts < 2)
                {
                    plugin.LogSearchRetry(current!, "No matching listing response within six seconds; retrying once after two seconds.");
                    Phase = ShoppingPhase.WaitingToSearch;
                    nextActionAt = now + TimeSpan.FromSeconds(2);
                    Status = $"Retrying listings for {current!.ItemName}...";
                }
                else
                    SkipCurrent("No matching live listings arrived after two search attempts.", now);
            }
            return;
        }

        // Idle returned above; the remaining active phase waits for purchase confirmation.
        if (now >= deadline)
        {
            Observe("Purchase confirmed before deadline", false, deadline.ToString("O", System.Globalization.CultureInfo.InvariantCulture), now.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            Fail($"Purchase confirmation timed out for {CurrentItemName}; stopped to avoid a duplicate purchase.");
        }
    }

    private void SearchNext(DateTime now)
    {
        if (current is null && queue.Count == 0)
        {
            Stop($"Shopping complete: {PurchasedCount:N0} purchased, {SkippedCount:N0} skipped, {Spent:N0} gil spent.");
            return;
        }

        diagnosticChecks.Clear();
        if (current is null)
        {
            current = queue.Dequeue();
            ownershipDeadline = null;
            listingAttempts = 0;
        }
        if (!CheckOwnership(current, now, "Ownership authorizes missing item"))
            return;
        if (!Observe("Listing service available", marketBoard.IsAvailable))
        {
            Fail("The Market Board listing service is unavailable.");
            return;
        }

        listingAttempts++;
        var accepted = marketBoard.RequestListings(current.ItemId, out requestId);
        foreach (var check in marketBoard.LastListingRequestChecks.Take(16))
            diagnosticChecks.Add(check);
        if (!Observe("Listing request accepted", accepted))
        {
            SkipCurrent("The game declined the listing request.", now);
            return;
        }

        listingResponseSeen = false;
        deadline = now + ListingTimeout;
        Phase = ShoppingPhase.WaitingForListings;
        Status = $"Finding live listings for {current.ItemName}...";
    }

    private void BuyCheapestCurrentListing(DateTime now)
    {
        // Only WaitingForListings reaches this method; that phase owns a current item.
        var item = current!;

        var listingsRead = Observe("Listings readable", marketBoard.TryReadListings(out var searchedItemId, out var candidates));
        var searchMatches = Observe("Search item matches", listingsRead ? searchedItemId == item.ItemId : null);
        if (!listingsRead || !searchMatches)
        {
            Fail("The Market Board changed to another item; stopped before spending gil.");
            return;
        }

        var choice = PurchasePolicy.SelectBestListing(
            candidates,
            item.ItemId,
            settings.EnableHighQualityPurchases);
        if (!Observe("Eligible single-item listing", choice is not null))
        {
            // A bounded sample identifies which row predicates excluded a listing
            // without exposing listing or retainer identities or copying the list.
            foreach (var candidate in candidates.Take(4))
            {
                var row = $"Row {candidate.SourceIndex}";
                Observe(row + " item matches", candidate.ItemId == item.ItemId);
                Observe(row + " listing identity present", candidate.ListingId != 0);
                Observe(row + " price nonzero", candidate.UnitPrice != 0);
                Observe(row + " quantity is one", candidate.Quantity == 1, "1", candidate.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Observe(row + " ordinary listing", !candidate.IsMannequin);
                Observe(row + " quality allowed", settings.EnableHighQualityPurchases || !candidate.IsHighQuality);
            }
            SkipCurrent(
                settings.EnableHighQualityPurchases
                    ? "No ordinary single-item listing is available."
                    : "No NQ single-item listing is available (HQ purchases are disabled).",
                now);
            return;
        }

        var selectedCandidate = choice!.Value;
        var bestTotal = selectedCandidate.TotalCost;
        if (!Observe("Per-item budget", PurchasePolicy.IsWithinPerItemCap(
                selectedCandidate,
                settings.MaximumPricePerItem,
                settings.UnlimitedPricePerItem), settings.UnlimitedPricePerItem ? "Unlimited" : settings.MaximumPricePerItem.ToString(System.Globalization.CultureInfo.InvariantCulture), bestTotal.ToString(System.Globalization.CultureInfo.InvariantCulture)))
        {
            SkipCurrent(
                $"Cheapest listing is {bestTotal:N0} gil after tax, above the "
                + $"{settings.MaximumPricePerItem:N0} cap.",
                now);
            return;
        }
        if (!Observe("Run budget", PurchasePolicy.IsWithinTotalCap(
                Spent,
                selectedCandidate,
                settings.MaximumTotalSpend,
                settings.UnlimitedTotalSpend), settings.UnlimitedTotalSpend ? "Unlimited" : settings.MaximumTotalSpend.ToString(System.Globalization.CultureInfo.InvariantCulture), (Spent + bestTotal).ToString(System.Globalization.CultureInfo.InvariantCulture)))
        {
            Fail($"Total budget reached before {item.ItemName}; {Spent:N0} gil spent.");
            return;
        }

        // Re-read and verify immediately before sending. Listing positions can change while shopping.
        var reread = marketBoard.ReadListing(selectedCandidate.SourceIndex);
        if (!Observe("Selected listing unchanged", reread == selectedCandidate))
        {
            Fail("The cheapest listing moved while it was being verified; stopped before spending gil.");
            return;
        }

        var verified = selectedCandidate;
        if (requestedItemIds.Contains(item.ItemId)
            || requestedListingIds.Contains(verified.ListingId))
        {
            Fail($"Blocked a duplicate purchase request for {item.ItemName}. Reload the plugin only after checking your inventory.");
            return;
        }

        // Listing identities remain bounded for the session; confirmed item guards transfer to ownership.
        if (requestedListingIds.Count >= MaximumRememberedPurchaseRequests)
        {
            Fail("The session purchase-safety registry reached its limit; reload only after checking your inventory.");
            return;
        }

        // Mark both IDs before entering native code. An ambiguous request is intentionally not
        // retryable during this plugin session; the player must inspect inventory first.
        if (!HasRunContext())
        {
            var reason = $"Stopped at {item.ItemName} ({item.ItemId}): character or world changed before purchase.";
            plugin.LogStopped(item, reason);
            Fail(reason);
            return;
        }
        if (!CheckOwnership(item, now, "Ownership still permits missing item"))
            return;
        requestedItemIds.Add(item.ItemId);
        requestedListingIds.Add(verified.ListingId);

        var submitted = marketBoard.SubmitPurchase(verified);
        foreach (var check in marketBoard.LastSubmissionChecks.Take(16))
            diagnosticChecks.Add(check);
        if (!Observe("Purchase request accepted", submitted))
        {
            Fail($"The game rejected the purchase request for {item.ItemName}.");
            return;
        }

        pendingCost = bestTotal;
        pendingListingId = verified.ListingId;
        pendingIsHighQuality = verified.IsHighQuality;
        deadline = now + PurchaseTimeout;
        Phase = ShoppingPhase.WaitingForPurchase;
        Status = $"Purchasing {item.ItemName} for {bestTotal:N0} gil...";
        plugin.LogRequested(item, verified);
    }

    private void OnOfferingsReceived(ShoppingOfferings offerings)
    {
        if (disposed || Phase != ShoppingPhase.WaitingForListings)
            return;

        var item = current!;
        var ours = offerings.ItemIds.Count > 0
            ? offerings.ItemIds.All(itemId => itemId == item.ItemId)
            : offerings.RequestId == requestId;
        if (!ours)
        {
            Observe("Listing response belongs to request", false);
            return;
        }

        Observe("Listing response belongs to request", true);
        listingResponseSeen = true;
        lastListingAt = utcNow();
    }

    private void OnItemPurchased(ShoppingPurchase purchase)
    {
        if (disposed || Phase != ShoppingPhase.WaitingForPurchase)
            return;

        if (!Observe("Run identity unchanged", HasRunContext()))
        {
            Fail("Character or world changed; automated purchasing stopped.");
            return;
        }

        var item = current!;
        // The response omits listing ID and price. Match every field it does carry,
        // including the raw catalog ID's HQ offset, before recording confirmation.
        var expectedCatalogId = (ulong)item.ItemId + (pendingIsHighQuality ? HighQualityItemIdOffset : 0);
        var baseMatches = Observe("Confirmation item matches", purchase.BaseItemId == item.ItemId);
        var quantityMatches = Observe("Confirmation quantity", purchase.ItemQuantity == 1, "1", purchase.ItemQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var qualityMatches = Observe("Confirmation quality matches", purchase.CatalogId == expectedCatalogId);
        if (!baseMatches || !quantityMatches || !qualityMatches)
        {
            Fail(
                $"An unexpected Market Board purchase event arrived (item {purchase.CatalogId}, quantity {purchase.ItemQuantity}); " +
                "stopped before making another request.");
            return;
        }

        plugin.LogConfirmed(item, pendingListingId, purchase.ItemQuantity);
        plugin.MarkPurchased(item.ItemId, runContentId);
        requestedItemIds.Remove(item.ItemId);
        Spent += pendingCost;
        PurchasedCount++;
        var purchasedName = item.ItemName;
        current = null;
        Phase = ShoppingPhase.WaitingToSearch;
        nextActionAt = utcNow()
            + TimeSpan.FromMilliseconds(Math.Max(
                MinimumActionDelayMilliseconds,
                settings.ActionDelayMilliseconds));
        Status = $"Purchased {purchasedName}. Preparing the next item...";
    }

    private bool CheckOwnership(ShoppingItem item, DateTime now, string checkName)
    {
        var ownership = plugin.ReadPurchaseReadiness(item, runContentId);
        if (ownershipDeadline is { } limit && now >= limit
            && (Phase == ShoppingPhase.WaitingForOwnership || !ownership.Known))
        {
            var reason = $"Stopped at {item.ItemName} ({item.ItemId}): ownership refresh timed out after 10 seconds. {ownership.Reason}";
            plugin.LogStopped(item, reason);
            Fail(reason);
            return false;
        }
        if (Observe(checkName, ownership is { Known: true, SuppressPurchase: false }))
            return true;
        if (ownership is { Known: true, SuppressPurchase: true })
            SkipCurrent(ownership.Reason, now);
        else
        {
            // Keep a single deadline for this item across repeated invalidations.
            // Recovery starts a new listing search, never reuses the old purchase candidate.
            ownershipDeadline ??= now + TimeSpan.FromSeconds(10);
            Phase = ShoppingPhase.WaitingForOwnership;
            Status = $"Waiting for ownership refresh before {item.ItemName}...";
        }
        return false;
    }

    private void SkipCurrent(string reason, DateTime now)
    {
        RetainFailure(reason);
        plugin.LogSkipped(current!, reason);
        var name = CurrentItemName;
        SkippedCount++;
        current = null;
        Phase = ShoppingPhase.WaitingToSearch;
        nextActionAt = now
            + TimeSpan.FromMilliseconds(Math.Max(
                MinimumActionDelayMilliseconds,
                settings.ActionDelayMilliseconds));
        Status = $"Skipped {name}: {reason}";
    }

    private bool IsMarketBoardOpen() => marketBoard.IsOpen;

    private void OnEventStreamFaulted()
    {
        if (!disposed && IsRunning)
            Fail("Market Board event processing exceeded its limit; stopped to avoid an uncertain purchase.");
    }

    private bool HasRunContext()
        => Observe("Character matches run", runContentId != 0 && plugin.CurrentContentId == runContentId)
            & Observe("World matches run", runWorldId != 0 && plugin.CurrentWorldId == runWorldId);
}
