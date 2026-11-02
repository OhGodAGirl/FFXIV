using System.Numerics;

namespace BetterGlamorPlates.Features.Examine;

/// <summary>Prepared Examine button colours keyed by the native ColorThemeType setting.</summary>
internal sealed record ExamineButtonTheme(Vector4 Background, Vector4 Hovered, Vector4 Active,
    Vector4 Border, Vector4 Icon, Vector4 SavedIcon, float Rounding)
{
    // Shared Game Theme Palettes reference, approved 2026-09-16. Local copy; no sibling dependency.
    internal static ExamineButtonTheme Resolve(uint theme) => theme switch
    {
        1 => Create(new(.94f, .81f, .65f, 1), true),
        2 => Create(new(.22f, .10f, .76f, 1), false, true),
        3 => Create(new(.22f, .32f, .55f, 1), false),
        4 => Create(new(.78f, .78f, .80f, 1), true),
        5 => Create(new(.27f, .46f, .16f, 1), false),
        6 => Create(new(.30f, .31f, .32f, 1), false),
        7 => Create(new(.93f, .63f, .85f, 1), true),
        _ => Create(new(.27f, .275f, .27f, 1), false),
    };

    private static ExamineButtonTheme Create(Vector4 background, bool light, bool classic = false)
    {
        var direction = light ? -1f : 1f;
        var icon = light ? new Vector4(.41f, .28f, .045f, 1) : new Vector4(.91f, .76f, .43f, 1);
        return new(background,
            background + new Vector4(.045f, .045f, .045f, 0) * direction,
            background + new Vector4(.08f, .08f, .08f, 0) * direction,
            light ? new(.48f, .40f, .27f, 1) : new(.72f, .62f, .42f, 1),
            icon, icon,
            classic ? 0 : 8);
    }
}
