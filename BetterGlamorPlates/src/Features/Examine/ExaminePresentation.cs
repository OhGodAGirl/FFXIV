using System.Numerics;
using BetterGlamorPlates.Adapters;
using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Features.Examine;

/// <summary>Presentation and enabled state derived solely from copied Examine facts.</summary>
internal static class ExaminePresentation
{
    internal static Vector2? Position(ExamineView? view, WindowBounds viewport)
        => view is null || view.OverlayOccluded ? null : ExamineTabPlacement.Calculate(view.Bounds, viewport);

    internal static bool CanSave(ExamineView view, bool saving)
        => !saving && view.UnavailableReason is null;

    internal static bool IsSaved(string status) => status.StartsWith("Saved ", StringComparison.Ordinal);

    internal static string Message(ExamineView view, bool saving, string status)
        => saving ? "Saving outfit..." : view.UnavailableReason ?? status;
}
