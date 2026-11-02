using System.Numerics;

namespace SpringCleaning;

internal static class DresserArmoireOverlayPolicy
{
    public static bool ShouldOpen(bool dresserOpen, bool requested)
    {
        return dresserOpen && requested;
    }

    public static Vector2 GetInitialPosition(
        Vector2 defaultPosition,
        bool rememberWindowPlacement,
        float? savedX,
        float? savedY)
    {
        return rememberWindowPlacement && savedX.HasValue && savedY.HasValue
            ? new Vector2(savedX.Value, savedY.Value)
            : defaultPosition;
    }
}
