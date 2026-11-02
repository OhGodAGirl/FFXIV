using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Adapters;

internal sealed record ExamineView(uint EntityId, string Name, WindowBounds Bounds, string? UnavailableReason = null)
{
    internal bool OverlayOccluded { get; init; }
}
