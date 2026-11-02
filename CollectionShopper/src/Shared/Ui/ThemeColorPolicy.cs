using System.Numerics;

namespace CollectionShopper;

internal static class ThemeColorPolicy
{
    internal static uint Normalize(uint theme) => theme <= 7 ? theme : 0;
    internal static Vector4 Apply(uint gameTheme, Vector4 original)
    {
        if (gameTheme == 0) return original;
        var palette = MarketBoardTabPresentation.ResolveTheme(gameTheme);
        var light = gameTheme is 1 or 4 or 7;
        // Preserve relative surface brightness and alpha across native theme families.
        if (original.X > .6f && original.Y > .6f && original.Z > .5f)
            return light ? new Vector4(.20f, .16f, .20f, original.W) : original;
        if (original.X > original.Z * 1.3f && original.X > .4f)
            return new Vector4(palette.Icon.X, palette.Icon.Y, palette.Icon.Z, original.W);
        var offset = (original.X + original.Y + original.Z) / 3f - .27f;
        var surfaceOffset = new Vector4(offset, offset, offset, 0f);
        var result = light ? palette.Surface - surfaceOffset : palette.Surface + surfaceOffset;
        return new Vector4(Math.Clamp(result.X, 0f, 1f), Math.Clamp(result.Y, 0f, 1f), Math.Clamp(result.Z, 0f, 1f), original.W);
    }
}
