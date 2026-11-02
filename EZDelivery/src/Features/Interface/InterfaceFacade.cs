namespace EZDelivery.Features.Interface;

/// <summary>Owns presentation state and a bounded input queue. It never calls native or IPC services.</summary>
public sealed class InterfaceFacade
{
    private readonly Queue<UiIntent> intents = new();
    private readonly object gate = new();
    private bool startQueued;
    /// <summary>Latest immutable state prepared outside rendering.</summary>
    public UiState State { get; private set; } = UiState.Empty;
    /// <summary>Selected browser category, or null when closed.</summary>
    public ShopCategory? SelectedCategory { get; private set; }
    /// <summary>Current browser search text.</summary>
    public string Search { get; private set; } = "";
    /// <summary>Cached projection of the selected category and search.</summary>
    public BrowserView Browser { get; private set; } = new(Array.Empty<ShopRow>(), Array.Empty<string>(), 0, 0, false);
    /// <summary>Whether the settings window is open.</summary>
    public bool SettingsOpen { get; set; }
    /// <summary>Whether the diagnostics window is open.</summary>
    public bool DiagnosticsOpen { get; set; }

    /// <summary>Framework-side preparation. Context loss closes the browser; the host stops its engine independently.</summary>
    public void Prepare(UiState state)
    {
        State = state;
        if (!state.LoggedIn || state.ShopAnchor is not { Ready: true })
        {
            SelectedCategory = null;
            Search = "";
        }
        Browser = Project(state.Shopping, SelectedCategory, Search);
    }

    /// <summary>Returns true for presentation-only intents. Closing/switching requires the caller to stop purchases.</summary>
    public bool HandlePresentationIntent(UiIntent intent)
    {
        if (intent.Generation != State.Generation) return false;
        switch (intent)
        {
            case SelectCategoryIntent selection when State.LoggedIn && State.ShopAnchor is { Ready: true }:
                SelectedCategory = SelectedCategory == selection.Category ? null : selection.Category;
                Search = "";
                break;
            case CloseBrowserIntent:
                SelectedCategory = null;
                break;
            case SearchIntent search:
                Search = search.Query.Length > 160 ? search.Query[..160] : search.Query;
                break;
            default: return false;
        }
        Browser = Project(State.Shopping, SelectedCategory, Search);
        return true;
    }

    /// <summary>Draw may enqueue only. Repeated starts before a drain collapse to one; Stop always has room.</summary>
    public void Enqueue(UiIntent intent)
    {
        lock (gate)
        {
            var starts = intent is StartDeliveryIntent or BuyOneIntent or BuyAllIntent;
            if (starts && startQueued) return;
            if (intent is StopDeliveryIntent or StopPurchasesIntent or CloseBrowserIntent && intents.Count >= 64)
                intents.Clear();
            if (intents.Count >= 64) return;
            if (intent is BuyAllIntent all)
                intent = all with { OfferKeys = Array.AsReadOnly(all.OfferKeys.Take(2048).ToArray()) };
            intents.Enqueue(intent);
            startQueued |= starts;
        }
    }

    /// <summary>Dequeues intents in order for framework-thread validation.</summary>
    public IReadOnlyList<UiIntent> DrainIntents()
    {
        lock (gate)
        {
            if (intents.Count == 0) return Array.Empty<UiIntent>();
            var result = intents.ToArray();
            intents.Clear();
            startQueued = false;
            return result;
        }
    }

    /// <summary>Filters missing rows and captures the exact Buy All offer keys.</summary>
    public static BrowserView Project(ShoppingView shopping, ShopCategory? category, string search)
    {
        if (category is null || !shopping.CollectionUsable || shopping.Rows.Count == 0)
            return new(Array.Empty<ShopRow>(), Array.Empty<string>(), 0, 0, search.Length > 0);
        var query = search.Trim();
        var visible = new List<ShopRow>();
        var keys = new List<string>();
        var preparedItems = new HashSet<uint>();
        var matchingCount = 0;
        ulong? total = 0;
        // Queue retains unaffordable entries: the purchase engine stops at the next unaffordable offer.
        // CanBuy describes native/provider eligibility, while affordability is checked separately.
        foreach (var row in shopping.Rows)
        {
            if (row.Category != category) continue;
            if (matchingCount == 2048) break;
            matchingCount++;
            if (!row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            visible.Add(row);
            if (!row.CanBuy || !preparedItems.Add(row.ItemId)) continue;
            keys.Add(row.OfferKey);
            // Preserve the entire displayed queue even if its aggregate cost cannot fit.
            if (total is { } sum) total = row.SealCost > ulong.MaxValue - sum ? null : sum + row.SealCost;
        }
        return new(visible.AsReadOnly(), keys.AsReadOnly(), total, matchingCount, search.Length > 0);
    }
}
