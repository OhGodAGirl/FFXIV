using BetterGlamorPlates.Features.GlamourPlates;

namespace BetterGlamorPlates.Features.Main;

internal sealed record SavedPlateSummary(
    Guid Id,
    string Name,
    DateTime UpdatedAtUtc,
    int FilledSlotCount)
{
    internal uint IconId { get; init; }
    internal bool HasUnsavedChanges { get; init; }
}

internal sealed record SavedSlotView(
    PlateSlot Slot,
    string SlotName,
    string ItemName,
    bool HasItem,
    uint IconId,
    SharedOwnershipState SharedOwnership,
    string? SharedOwnershipLabel)
{
    internal byte Stain0Id { get; init; }
    internal byte Stain1Id { get; init; }
    internal uint ItemId { get; init; }
    internal byte DyeCount { get; init; }
    internal bool DyeFactsAvailable { get; init; }
    internal bool MissingFromLastLoad { get; init; }
}

internal sealed record MainViewState(
    IReadOnlyList<SavedPlateSummary> SavedPlates,
    int TotalSavedPlateCount,
    SavedPlateSummary? ActivePlate,
    Guid? SelectedPlateId,
    IReadOnlyList<SavedSlotView> Slots,
    IReadOnlyList<CollectionItem> VisibleItems,
    int CollectionCount,
    PlateSlot SelectedSlot,
    string PlateSearch,
    string CollectionFilter,
    CollectionSourceFilter CollectionSources,
    Guid? DeleteArmedId,
    bool CanLoad,
    string? LoadDisabledReason,
    string PrimaryStatus,
    string Status,
    string SharedCollectionStatus,
    SharedCollectionDiagnostics SharedCollectionDiagnostics,
    SharedPlateCompleteness SharedPlateCompleteness)
{
    internal AccessoriesView Accessories { get; init; } = new([], 0, SharedOwnershipState.Empty, "Empty");
    internal PlateSlot SelectedAccessory { get; init; } = PlateSlot.Ears;
    internal long OutfitRevision { get; init; }
    internal bool HasUnsavedChanges { get; init; }
    internal string? OperationFeedback { get; init; }
    internal bool HasCollectionObservation { get; init; }
    internal PlateDyeMode PreviewDyeMode { get; init; } = PlateDyeMode.Saved;
    internal bool CollectionNeedsAttention { get; init; }
    internal string EmptyCollectionMessage { get; init; } = "Ownership is unavailable. Your local plates are still available.";

    internal static MainViewState Empty { get; } = new(
        [],
        0,
        null,
        null,
        [],
        [],
        0,
        PlateSlot.Body,
        string.Empty,
        string.Empty,
        CollectionSourceFilter.All,
        null,
        false,
        "Select or create a saved plate first.",
        "Collection not available yet.",
        "Select a saved plate or create a new one.",
        "Collection not available yet.",
        SharedCollectionDiagnostics.Empty,
        SharedPlateCompleteness.Empty);
}
