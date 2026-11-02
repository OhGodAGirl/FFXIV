using System.Globalization;

namespace SpringCleaning;

internal sealed record MigrationDiagnosticSnapshot(
    string Build,
    MigrationMode Mode,
    MigrationPhase Phase,
    string Status,
    string? CurrentItem,
    int StoredCount,
    int TotalPieceCount,
    int EligibleCount,
    uint PrismsHeld,
    int PrismsRequired,
    bool PrismCountReady,
    bool DresserLoaded,
    bool CabinetLoaded,
    bool CandidateListReady,
    bool DresserOpen,
    bool ArmoireOpen,
    bool GlamourCreationOpen,
    bool OutfitCreationOpen,
    bool OutfitConfirmationOpen,
    bool ItemConfirmationOpen,
    bool InventoryContextOpen,
    bool ExistingOutfitPromptOpen,
    int PendingOutfits,
    int PendingItems,
    int SourceCategoryPosition,
    int SourceCategoryCount,
    int InteractionAttempts,
    int DresserOpenAttempts,
    bool NavigationIssued,
    bool InteractionIssued,
    bool MenuSelectionIssued,
    bool ConfirmationIssued)
{
    public bool IsActive => Phase is not MigrationPhase.Idle
        and not MigrationPhase.Complete
        and not MigrationPhase.Paused
        and not MigrationPhase.Cancelled;

    public string Fingerprint => string.Join(
        '|',
        Mode,
        Phase,
        Status,
        CurrentItem,
        StoredCount,
        TotalPieceCount,
        EligibleCount,
        PrismsHeld,
        PrismsRequired,
        PrismCountReady,
        DresserLoaded,
        CabinetLoaded,
        CandidateListReady,
        DresserOpen,
        ArmoireOpen,
        GlamourCreationOpen,
        OutfitCreationOpen,
        OutfitConfirmationOpen,
        ItemConfirmationOpen,
        InventoryContextOpen,
        ExistingOutfitPromptOpen,
        PendingOutfits,
        PendingItems,
        SourceCategoryPosition,
        SourceCategoryCount,
        InteractionAttempts,
        DresserOpenAttempts,
        NavigationIssued,
        InteractionIssued,
        MenuSelectionIssued,
        ConfirmationIssued);

    public string ToActivitySummary() =>
        $"{Mode}/{Phase}: {Status}"
        + $" | item={CurrentItem ?? "none"}"
        + $" | stored={StoredCount}/{TotalPieceCount}"
        + $" | queues={PendingOutfits} outfits,{PendingItems} items"
        + $" | prisms={(PrismCountReady ? PrismsHeld.ToString(CultureInfo.InvariantCulture) : "unknown")}/{PrismsRequired}"
        + $" | ui={FormatOpenWindows()}"
        + $" | attempts={InteractionAttempts}/{DresserOpenAttempts}";

    public IReadOnlyList<string> ToReportLines() =>
            [
                $"Build: {Build}",
        $"Mode: {Mode}",
        $"Phase: {Phase}",
        $"Status: {Status}",
        $"Current item: {CurrentItem ?? "None"}",
        $"Progress: {StoredCount} stored / {TotalPieceCount} planned; {EligibleCount} eligible",
        $"Prisms: {(PrismCountReady ? PrismsHeld.ToString(CultureInfo.InvariantCulture) : "unavailable")} held / {PrismsRequired} planned",
        $"Data: dresser={(DresserLoaded ? "ready" : "not ready")}; armoire={(CabinetLoaded ? "ready" : "not ready")}; candidates={(CandidateListReady ? "ready" : "refreshing")}",
        $"Native UI: {FormatOpenWindows()}",
        $"Queues: {PendingOutfits} outfits; {PendingItems} items; source category {SourceCategoryPosition}/{SourceCategoryCount}",
        $"Attempts: interaction={InteractionAttempts}; dresser-open={DresserOpenAttempts}",
        $"Requests: navigation={NavigationIssued}; interaction={InteractionIssued}; menu={MenuSelectionIssued}; confirmation={ConfirmationIssued}",
    ];

    private string FormatOpenWindows()
    {
        var windows = new List<string>();
        AddWindow(windows, DresserOpen, "dresser");
        AddWindow(windows, ArmoireOpen, "armoire");
        AddWindow(windows, GlamourCreationOpen, "creation");
        AddWindow(windows, OutfitCreationOpen, "outfit");
        AddWindow(windows, OutfitConfirmationOpen, "outfit-confirm");
        AddWindow(windows, ItemConfirmationOpen, "item-confirm");
        AddWindow(windows, InventoryContextOpen, "context");
        AddWindow(windows, ExistingOutfitPromptOpen, "merge-prompt");
        return windows.Count == 0 ? "none" : string.Join(',', windows);
    }

    private static void AddWindow(List<string> windows, bool open, string name)
    {
        if (open)
            windows.Add(name);
    }
}
