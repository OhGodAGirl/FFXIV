namespace BetterGlamorPlates.Features.GlamourPlates;

internal enum PlateSlot
{
    MainHand = 0,
    OffHand = 1,
    Head = 2,
    Body = 3,
    Hands = 4,
    Legs = 5,
    Feet = 6,
    Ears = 7,
    Neck = 8,
    Wrists = 9,
    RightRing = 10,
    LeftRing = 11,
}

internal enum CollectionSource
{
    Dresser,
    Armoire,
}

[Flags]
internal enum CollectionSourceFilter
{
    None = 0,
    Dresser = 1 << 0,
    Armoire = 1 << 1,
    All = Dresser | Armoire,
}

internal enum CollectionFreshness
{
    Fresh,
    LastKnown,
}

internal sealed record CollectionItem(
    uint ItemId,
    string Name,
    uint IconId,
    CollectionSource Source,
    CollectionFreshness Freshness,
    string FreshnessLabel,
    EquipSlotMask SlotMask)
{
    internal string StableKey => $"{Source}:{ItemId}";
}

internal sealed record ItemDisplayFacts(string Name, uint IconId, EquipSlotMask SlotMask)
{
    internal byte DyeCount { get; init; }
}

internal sealed record GameIconOption(uint Id, string Name, string Category = "Other");

internal sealed record DyeOption(byte Id, string Name, uint Color, bool SpecialReagent = false, string Materials = "");

internal sealed record NativeCollectionItem(
    uint ItemId,
    CollectionSource Source,
    uint SourceId,
    byte Stain0Id,
    byte Stain1Id);

/// <summary>
/// Stores one item appearance in a local plate configuration.
/// </summary>
public sealed class SavedPlateItem
{
    /// <summary>
    /// Gets or sets the base FFXIV item identifier.
    /// </summary>
    public uint ItemId { get; set; }

    /// <summary>
    /// Gets or sets the first saved stain identifier.
    /// </summary>
    public byte Stain0Id { get; set; }

    /// <summary>
    /// Gets or sets the second saved stain identifier.
    /// </summary>
    public byte Stain1Id { get; set; }
}

/// <summary>
/// Stores one unlimited local Glamour Plate configuration.
/// </summary>
public sealed class SavedPlate
{
    /// <summary>Stable library position; absent on older configurations.</summary>
    public int? LibraryOrder { get; set; }
    /// <summary>
    /// Gets or sets the stable local plate identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the visible plate name.
    /// </summary>
    public string Name { get; set; } = "New Plate";

    /// <summary>Gets or sets the optional game icon; zero uses the outfit fallback.</summary>
    public uint IconId { get; set; }

    /// <summary>
    /// Gets or sets the twelve native Glamour Plate slots.
    /// </summary>
    public SavedPlateItem?[] Items { get; set; } = new SavedPlateItem?[PlateRules.SlotCount];

    /// <summary>
    /// Gets or sets when the plate was last changed.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }
}

internal sealed record NativeCollectionSnapshot(
    IReadOnlyList<NativeCollectionItem> Items,
    bool IsDresserLoaded,
    bool IsArmoireLoaded,
    DateTime CapturedAtUtc)
{
    internal static NativeCollectionSnapshot Empty { get; } = new([], false, false, DateTime.MinValue);
}

internal sealed record SharedCollectionSourceStatus(
    string Name,
    string ProviderStatus,
    string Detail);

internal sealed record SharedCollectionDiagnostics(
    string Guidance,
    SharedCollectionSourceStatus GlamourDresser,
    SharedCollectionSourceStatus Armoire)
{
    internal static SharedCollectionDiagnostics Empty { get; } = new(
        "Exportaramus is unavailable. Ownership remains unknown; Better Glamour Plates will not treat it as an empty collection.",
        new SharedCollectionSourceStatus("Glamour Dresser", "Not Available", "Not checked yet."),
        new SharedCollectionSourceStatus("Armoire", "Not Available", "Not checked yet."));
}

internal enum SharedOwnershipState
{
    Empty,
    FreshMatch,
    LastKnownMatch,
    NotObserved,
    Unknown,
}

internal sealed record SharedPlateCompleteness(
    int FilledSlots,
    int FreshMatches,
    int LastKnownMatches,
    int NotObserved,
    int Unknown,
    string Summary)
{
    internal static SharedPlateCompleteness Empty { get; } = new(
        0,
        0,
        0,
        0,
        0,
        "Select a saved plate to compare it with Exportaramus ownership.");
}

internal enum PlateCaptureOutcome
{
    Captured,
    EditorNotReady,
    PendingDyes,
}

internal sealed record PlateCaptureResult(PlateCaptureOutcome Outcome, SavedPlate? Plate);

internal enum PlateApplyFailureKind
{
    EditorNotReady,
    EditorClosed,
    ClearRejected,
    ItemRejected,
    EditorClosedAfterPartial,
    DyeUnavailable,
    UnsupportedDye,
    DyeRejected,
    PendingDyes,
}

internal sealed record PlateApplyFailure(
    PlateApplyFailureKind Kind,
    PlateSlot? Slot = null,
    uint ItemId = 0)
{
    internal byte DyeId { get; init; }
    internal byte? DyeChannel { get; init; }
}

internal sealed record PlateApplyResult(
    int Loaded,
    int Cleared,
    IReadOnlyList<PlateApplyFailure> Failures)
{
    internal bool IsComplete => Failures.Count == 0;
}

