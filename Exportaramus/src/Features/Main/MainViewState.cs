using System.Collections.Immutable;
using Exportaramus.Features.GuidedExport;

namespace Exportaramus.Features.Main;

public sealed record MainViewState(long Revision, MainStatus Status, string StateLabel, UiTone StateTone,
    string LastRefresh, bool ReduceMotion, bool CanSync, bool CanGuide, bool CanCopy,
    int HistoryCount, string LatestSnapshot, string HealthHeadline, UiTone HealthTone,
    ImmutableArray<HealthFact> HealthFacts, ImmutableArray<CollectionRowView> Rows,
    string Message, GuidedView Guided)
{
    public Exportaramus.Features.Inventory.InventoryPresentation Inventory { get; init; } = Exportaramus.Features.Inventory.InventoryPresentation.Empty;
    public string HealthTooltip { get; init; } = "";
    public bool RetainersActive { get; init; }
    public string RetainersMessage { get; init; } = "";
    public bool HasSnapshot => !Rows.IsEmpty;
    public string CollectionCountText { get; } = $"{Rows.Length} Collections";
    public string HistoryCountText { get; } = $"{HistoryCount} Snapshots Retained";
    public string LatestSnapshotText { get; } = $"Latest: {LatestSnapshot}";
}
