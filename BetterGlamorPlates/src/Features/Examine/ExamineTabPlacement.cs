using System.Numerics;
using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Features.Examine;

/// <summary>Places the save icon below facewear, opposite the native soul crystal slot.</summary>
internal static class ExamineTabPlacement
{
    internal static bool IsCovered(uint anchorLayer, int anchorIndex, uint layer, int index,
        WindowBounds icon, WindowBounds window)
        => (layer > anchorLayer || (layer == anchorLayer && index < anchorIndex))
            && window.Width > 0 && window.Height > 0
            && icon.X < window.X + window.Width && icon.X + icon.Width > window.X
            && icon.Y < window.Y + window.Height && icon.Y + icon.Height > window.Y;

    internal static Vector2 ButtonSize(WindowBounds examine) => new Vector2(64) * Scale(examine);
    internal static float Scale(WindowBounds examine) => Math.Min(examine.Width / 620, examine.Height / 960);

    internal static Vector2? Calculate(WindowBounds examine, WindowBounds viewport)
    {
        if (!float.IsFinite(examine.X + examine.Y + examine.Width + examine.Height
                + viewport.X + viewport.Y + viewport.Width + viewport.Height)
            || examine.Width <= 0 || examine.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0)
            return null;
        var size = ButtonSize(examine);
        // Align with the left equipment column and the soul crystal row. Native
        // addon bounds include bottom padding beyond the visible frame.
        var position = new Vector2(examine.X + 79 * Scale(examine) - size.X / 2,
            examine.Y + examine.Height - 136 * Scale(examine));
        // Never clamp onto another native control when the footer is off-screen.
        return position.X >= viewport.X && position.Y >= viewport.Y
            && position.X + size.X <= viewport.X + viewport.Width && position.Y + size.Y <= viewport.Y + viewport.Height
            ? position : null;
    }
}
