using System.Numerics;

namespace SpringCleaning;

internal static class DrawerPlacementPolicy
{
    internal static Vector2 ClampPosition(Vector2 position, Vector2 size, Vector2 viewportPosition, Vector2 viewportSize)
    {
        var minimum = viewportPosition + new Vector2(8f);
        var maximum = Vector2.Max(minimum, viewportPosition + viewportSize - size - new Vector2(8f));
        return Vector2.Clamp(position, minimum, maximum);
    }

    internal readonly record struct Layout(Vector2 Position, Vector2 Size, Vector2 CreationPosition, bool FitsBeside);

    internal static Layout Plan(Vector2 dresserPosition, Vector2 dresserSize, Vector2 viewportPosition,
        Vector2 viewportSize, Vector2 creationSize, float scale)
    {
        scale = float.IsFinite(scale) ? Math.Clamp(scale, 0.5f, 3f) : 1f;
        var gap = 8f * scale;
        var width = MathF.Min(360f * scale, MathF.Max(1f, viewportSize.X - gap * 2f));
        var height = MathF.Min(MathF.Max(260f * scale, dresserSize.Y), MathF.Max(1f, viewportSize.Y - gap * 2f));
        var right = viewportPosition.X + viewportSize.X;
        var bottom = viewportPosition.Y + viewportSize.Y;
        var position = new Vector2(dresserPosition.X + dresserSize.X + gap,
            Math.Clamp(dresserPosition.Y, viewportPosition.Y + gap, MathF.Max(viewportPosition.Y + gap, bottom - height - gap)));
        var creation = new Vector2(position.X + width + gap, position.Y);
        var fits = creation.X + creationSize.X <= right - gap;
        // Keep the drawer reachable without resizing native windows. At narrow widths it docks
        // below the native pair if possible; otherwise its right-edge placement is reversible.
        if (!fits)
        {
            var below = dresserPosition.Y + dresserSize.Y + gap;
            position.X = Math.Clamp(dresserPosition.X + gap, viewportPosition.X + gap, MathF.Max(viewportPosition.X + gap, right - width - gap));
            if (below + 260f * scale <= bottom - gap)
            {
                position.Y = below;
                height = bottom - below - gap;
            }
        }
        return new Layout(position, new Vector2(width, height), creation, fits);
    }
}
