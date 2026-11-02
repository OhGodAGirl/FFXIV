namespace CollectionShopper;

internal sealed partial class Configuration
{
    internal const int CurrentVersion = ConfigurationMigrationPolicy.CurrentVersion;
    internal const int DefaultActionDelayMilliseconds = ConfigurationMigrationPolicy.DefaultActionDelayMilliseconds;
    internal const int LegacyActionDelayMilliseconds = ConfigurationMigrationPolicy.LegacyActionDelayMilliseconds;

    public int Version { get; set; } = CurrentVersion;
    public bool HideLocallyOwned { get; set; } = true;
    public bool HideCompletedPurchases { get; set; } = true;
    public bool AutomaticallyRefreshCollection { get; set; } = true;
    public bool ShowOutfitsTab { get; set; } = true;
    public bool ShowWeaponsTab { get; set; } = true;
    public bool ShowMinionsTab { get; set; } = true;
    public bool ShowMountsTab { get; set; } = true;
    public bool ShowOrchestrionsTab { get; set; } = true;
    public bool ShowHairstylesTab { get; set; } = true;
    public bool ShowFacewearTab { get; set; } = true;
    public bool ShowFashionTab { get; set; } = true;
    public MarketBoardTabPresentation.TabStyle MarketBoardTabStyle { get; set; }
        = MarketBoardTabPresentation.TabStyle.RoundTabs;
    public int ActionDelayMilliseconds { get; set; } = DefaultActionDelayMilliseconds;
    // Legacy purchase archive; no longer establishes current ownership.
    public HashSet<uint> CompletedItemIds { get; set; } = [];
    public List<PendingPurchase> PurchaseTracking { get; set; } = [];
    public int MaximumPricePerItem { get; set; }
    public bool UnlimitedPricePerItem { get; set; }
    public int MaximumTotalSpend { get; set; }
    public bool UnlimitedTotalSpend { get; set; }
    public bool EnableHighQualityPurchases { get; set; }
    public bool ReduceMotion { get; set; }
    public bool PurchaseAllMissingOutfits { get; set; }
    public bool PurchaseAllMissingWeapons { get; set; }
    public bool PurchaseAllMissingMinions { get; set; }
    public bool PurchaseAllMissingMounts { get; set; }
    public bool PurchaseAllMissingOrchestrions { get; set; }
    public bool PurchaseAllMissingHairstyles { get; set; }
    public bool PurchaseAllMissingFacewear { get; set; }
    public bool PurchaseAllMissingFashion { get; set; }

}
