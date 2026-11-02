using System.Numerics;
using BetterGlamorPlates.Features.Examine;

namespace BetterGlamorPlates.Tests.Features.Examine;

/// <summary>Checks theme fallback, differentiation and icon contrast.</summary>
public sealed class ExamineButtonThemeTests
{
    /// <summary>Unrecognized settings retain the safe default appearance.</summary>
    [Fact]
    public void UnknownThemeUsesDarkFallback()
        => Assert.Equal(ExamineButtonTheme.Resolve(0), ExamineButtonTheme.Resolve(uint.MaxValue));

    /// <summary>All eight native selections receive distinct surfaces.</summary>
    [Fact]
    public void EachNativeThemeHasDistinctSurface()
        => Assert.Equal(8, Enumerable.Range(0, 8).Select(i => ExamineButtonTheme.Resolve((uint)i).Background).Distinct().Count());

    /// <summary>Approved foreground and interaction transformations apply to every theme.</summary>
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(4u)]
    [InlineData(5u)]
    [InlineData(6u)]
    [InlineData(7u)]
    public void UsesApprovedForegroundAndInteractionStates(uint theme)
    {
        var palette = ExamineButtonTheme.Resolve(theme);
        var light = theme is 1 or 4 or 7;
        var direction = light ? -1f : 1f;
        Assert.Equal(palette.Background + new Vector4(.045f, .045f, .045f, 0) * direction, palette.Hovered);
        Assert.Equal(palette.Background + new Vector4(.08f, .08f, .08f, 0) * direction, palette.Active);
        Assert.Equal(light ? new Vector4(.48f, .40f, .27f, 1) : new Vector4(.72f, .62f, .42f, 1), palette.Border);
        Assert.Equal(light ? new Vector4(.41f, .28f, .045f, 1) : new Vector4(.91f, .76f, .43f, 1), palette.Icon);
        Assert.Equal(palette.Icon, palette.SavedIcon);
        // The approved dark-theme golds are visual approximations, not a universal contrast guarantee.
        if (light)
            foreach (var surface in new[] { palette.Background, palette.Hovered, palette.Active })
                Assert.True(Contrast(surface, palette.Icon) >= 3);
        Assert.Equal(theme == 2 ? 0 : 8, palette.Rounding);
        Assert.NotEqual(palette.Background, palette.Hovered);
        Assert.NotEqual(palette.Hovered, palette.Active);
    }

    /// <summary>Locks the user-approved base values, including final Clear White.</summary>
    [Theory]
    [InlineData(0u, .27f, .275f, .27f)]
    [InlineData(1u, .94f, .81f, .65f)]
    [InlineData(2u, .22f, .10f, .76f)]
    [InlineData(3u, .22f, .32f, .55f)]
    [InlineData(4u, .78f, .78f, .80f)]
    [InlineData(5u, .27f, .46f, .16f)]
    [InlineData(6u, .30f, .31f, .32f)]
    [InlineData(7u, .93f, .63f, .85f)]
    public void UsesApprovedSurface(uint theme, float red, float green, float blue)
        => Assert.Equal(new Vector4(red, green, blue, 1), ExamineButtonTheme.Resolve(theme).Background);

    private static double Contrast(Vector4 first, Vector4 second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    private static double Luminance(Vector4 colour)
    {
        static double Linear(float value) => value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        return .2126 * Linear(colour.X) + .7152 * Linear(colour.Y) + .0722 * Linear(colour.Z);
    }
}
