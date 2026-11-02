using System.Numerics;

namespace BetterGlamorPlates.Features.Main;

/// <summary>Ordinary screen-space bounds, with no retained native address.</summary>
internal readonly record struct WindowBounds(float X, float Y, float Width, float Height);

internal readonly record struct CompanionPlacement(Vector2 Position, Vector2 Size, float Scale);

/// <summary>Fits beside the native preview without covering it.</summary>
internal static class CompanionPlacementPolicy
{
    internal static CompanionPlacement? Calculate(WindowBounds native, WindowBounds viewport, float drawerWidth = 0)
    {
        if (!IsValid(native) || !IsValid(viewport) || viewport.Width <= 8 || viewport.Height <= 8)
            return null;

        // Reserve the native preview: shrink into free side space instead of clamping over it.
        var right = Math.Max(viewport.X + 4, native.X + native.Width + 4);
        var leftWidth = Math.Max(0, Math.Min(native.X - 4, viewport.X + viewport.Width - 4) - (viewport.X + 4));
        var rightWidth = Math.Max(0, viewport.X + viewport.Width - 4 - right);
        var useRight = rightWidth >= leftWidth;
        var availableWidth = useRight ? rightWidth : leftWidth;
        if (availableWidth <= 8)
            return null;

        var height = Math.Min(native.Height, viewport.Height - 8);
        var scale = Math.Min(height / MainLayoutPolicy.ReferenceHeight,
            availableWidth / (MainLayoutPolicy.ReferenceWidth + Math.Max(0, drawerWidth)));
        var size = new Vector2(Math.Min(availableWidth, MainLayoutPolicy.ReferenceWidth * scale), MainLayoutPolicy.ReferenceHeight * scale);
        var x = useRight ? right : Math.Min(native.X - 4, viewport.X + viewport.Width - 4) - size.X - Math.Max(0, drawerWidth) * scale;
        return new CompanionPlacement(
            new Vector2(x, Math.Clamp(native.Y, viewport.Y + 4, viewport.Y + viewport.Height - 4 - size.Y)),
            size, scale);
    }
    private static bool IsValid(WindowBounds value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y)
            && float.IsFinite(value.Width) && float.IsFinite(value.Height)
            && value.Width > 0 && value.Height > 0;
}


