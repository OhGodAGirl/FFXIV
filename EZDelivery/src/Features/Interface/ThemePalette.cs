using System.Numerics;
namespace EZDelivery.Features.Interface;

internal sealed record TabPalette(Vector4 Surface, Vector4 Hover, Vector4 Active, Vector4 Border, Vector4 Icon, Vector4 Inset);
internal static class ThemePalette
{
    internal static TabPalette Resolve(uint theme)
    {
        var light = theme is 1 or 4 or 7;
        var surface = theme switch
        {
            1 => new Vector4(.94f, .81f, .65f, 1f),
            2 => new Vector4(.22f, .10f, .76f, 1f),
            3 => new Vector4(.22f, .32f, .55f, 1f),
            4 => new Vector4(.78f, .78f, .80f, 1f),
            5 => new Vector4(.27f, .46f, .16f, 1f),
            6 => new Vector4(.30f, .31f, .32f, 1f),
            7 => new Vector4(.93f, .63f, .85f, 1f),
            _ => new Vector4(.27f, .275f, .27f, 1f),
        };
        var direction = light ? -1f : 1f;
        return new(surface,
            surface + new Vector4(.045f, .045f, .045f, 0f) * direction,
            surface + new Vector4(.08f, .08f, .08f, 0f) * direction,
            light ? new(.48f, .40f, .27f, 1f) : new(.72f, .62f, .42f, 1f),
            light ? new(.41f, .28f, .045f, 1f) : new(.91f, .76f, .43f, 1f),
            surface - new Vector4(.04f, .04f, .04f, 0f));
    }


    internal static Vector4 Transform(uint gameTheme, Vector4 original)
    {
        gameTheme = gameTheme <= 7 ? gameTheme : 0;
        if (gameTheme == 0) return original;
        var palette = ThemePalette.Resolve(gameTheme);
        var light = gameTheme is 1 or 4 or 7;
        // Preserve relative surface brightness and alpha across native theme families.
        if (original.X > .6f && original.Y > .6f && original.Z > .5f)
            return light ? new Vector4(.20f, .16f, .20f, original.W) : original;
        if (original.X > original.Z * 1.3f && original.X > .4f)
            return new Vector4(palette.Icon.X, palette.Icon.Y, palette.Icon.Z, original.W);
        var offset = (original.X + original.Y + original.Z) / 3f - .27f;
        var result = palette.Surface + new Vector4(offset, offset, offset, 0f) * (light ? -1f : 1f);
        return new Vector4(Math.Clamp(result.X, 0f, 1f), Math.Clamp(result.Y, 0f, 1f), Math.Clamp(result.Z, 0f, 1f), original.W);
    }
}
