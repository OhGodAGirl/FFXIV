using Dalamud.Configuration;

namespace SpringCleaning;

internal sealed class Configuration : IPluginConfiguration
{
    internal const int DefaultActionDelayMilliseconds = 750;
    internal const int DefaultConfirmationTimeoutMilliseconds = 8_000;
    internal const float DefaultInteractionDistanceYalms = 2.0f;

    public BookmarkTabStyle TabStyle { get; set; } = BookmarkTabStyle.RoundTabs;

    public int Version { get; set; } = 1;

    public int ActionDelayMilliseconds { get; set; } = DefaultActionDelayMilliseconds;

    public int ConfirmationTimeoutMilliseconds { get; set; } = DefaultConfirmationTimeoutMilliseconds;

    public float InteractionDistance { get; set; } = DefaultInteractionDistanceYalms;

    public bool ProtectMateriaAndFullySpiritboundItems { get; set; } = true;

    private bool? protectItemsWithMateria;
    private bool? protectItemsWithSpiritbond;

    // Missing new keys inherit the legacy preference regardless of JSON property order.
    public bool ProtectItemsWithMateria
    {
        get => protectItemsWithMateria ?? ProtectMateriaAndFullySpiritboundItems;
        set => protectItemsWithMateria = value;
    }

    public bool ProtectItemsWithSpiritbond
    {
        get => protectItemsWithSpiritbond ?? ProtectMateriaAndFullySpiritboundItems;
        set => protectItemsWithSpiritbond = value;
    }

    public HashSet<uint> IgnoredDuplicateItemIds { get; set; } = [];

    public int ReservedInventorySlots { get; set; }

    public bool OnlyMoveCompleteOutfits { get; set; }

    public bool SkipExistingDresserOutfits { get; set; }

    public bool ReviewOnlyItemsOutsideOutfits { get; set; }

    public bool DiagnosticMode { get; set; }

    public bool RememberWindowPlacement { get; set; }

    public float? DresserArmoireOverlayX { get; set; }

    public float? DresserArmoireOverlayY { get; set; }
}

internal enum BookmarkTabStyle
{
    InsetSockets,
    FlushIndex,
    RibbonTabs,
    RoundTabs,
    BeveledIndex,
}
