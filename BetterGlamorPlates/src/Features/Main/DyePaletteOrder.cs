using BetterGlamorPlates.Features.GlamourPlates;

namespace BetterGlamorPlates.Features.Main;

/// <summary>Groups neutrals, then neighbouring hues, with light shades first.</summary>
internal static class DyePaletteOrder
{
    internal static DyeOption[] Sort(IEnumerable<DyeOption> dyes)
        => dyes.Take(255).OrderBy(dye => Key(dye.Color)).ThenBy(dye => dye.Id).ToArray();

    private static (int Family, float Lightness, float Hue) Key(uint color)
    {
        var r = ((color >> 16) & 255) / 255f;
        var g = ((color >> 8) & 255) / 255f;
        var b = (color & 255) / 255f;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var lightness = -(max + min) / 2;
        if (delta < 0.08f) return (0, lightness, 0);
        var hue = max == r ? (g - b) / delta : max == g ? 2 + (b - r) / delta : 4 + (r - g) / delta;
        hue = (hue * 60 + 360) % 360;
        return (1 + (int)(hue / 30), lightness, hue);
    }
}
