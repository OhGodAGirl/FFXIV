using System.Numerics;
using Exportaramus.Shared.Ui;

namespace Exportaramus.Tests.Main;

public sealed class ThemePolicyTests
{
    [Fact]
    public void UnknownAndUnavailableSelectionsUseDefaultDarkPalette()
    {
        Assert.Same(ThemePolicy.Resolve(0), ThemePolicy.Resolve(null));
        Assert.Same(ThemePolicy.Resolve(0), ThemePolicy.Resolve(8));
        Assert.Same(ThemePolicy.Resolve(0), ThemePolicy.Resolve(uint.MaxValue));
        Assert.Equal(new Vector4(.18f, .182f, .185f, .99f), ThemePolicy.Resolve(0).Window);
    }

    [Fact]
    public void EightSelectionsHaveDistinctCachedOpaqueSurfaces()
    {
        var palettes = Enumerable.Range(0, 8).Select(i => ThemePolicy.Resolve((uint)i)).ToArray();
        Assert.Equal(8, palettes.Select(p => p.Window).Distinct().Count());
        for (uint i = 0; i < 8; i++)
        {
            Assert.Same(palettes[i], ThemePolicy.Resolve(i));
            Assert.Equal(.99f, palettes[i].Window.W);
            Assert.Equal(1, palettes[i].Panel.W);
        }
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(4u)]
    [InlineData(5u)]
    [InlineData(6u)]
    [InlineData(7u)]
    public void BodyAndSemanticTextRemainReadableOnPanelSurfaces(uint selection)
    {
        var p = ThemePolicy.Resolve(selection);
        Assert.True(Contrast(p.Icon, p.Panel) >= 3, "Shared non-text icons need 3:1 contrast.");
        foreach (var foreground in new[] { p.Primary, p.Secondary, p.Muted, p.Heading, p.Count, p.Success, p.Warning, p.Error })
            Assert.True(Contrast(foreground, p.Panel) >= 4.5, $"Theme {selection}: {foreground} lacks 4.5:1 contrast.");
    }

    [Theory]
    [InlineData(0u, .27f, .275f, .27f)]
    [InlineData(1u, .94f, .81f, .65f)]
    [InlineData(2u, .22f, .10f, .76f)]
    [InlineData(3u, .22f, .32f, .55f)]
    [InlineData(4u, .78f, .78f, .80f)]
    [InlineData(5u, .27f, .46f, .16f)]
    [InlineData(6u, .30f, .31f, .32f)]
    [InlineData(7u, .93f, .63f, .85f)]
    public void ApprovedBasesAndInteractionColorsStayBounded(uint id, float red, float green, float blue)
    {
        var palette = ThemePolicy.Resolve(id);
        Assert.Equal(new Vector4(red, green, blue, 1), palette.Surface);
        Assert.NotEqual(palette.Raised, palette.ButtonHovered);
        Assert.NotEqual(palette.ButtonHovered, palette.ButtonActive);
        foreach (var property in typeof(ThemePalette).GetProperties())
        {
            var color = (Vector4)property.GetValue(palette)!;
            foreach (var component in new[] { color.X, color.Y, color.Z, color.W }) Assert.InRange(component, 0, 1);
        }
        if (id is 1 or 4 or 7)
        {
            Assert.Equal(new Vector4(.20f, .16f, .20f, 1), palette.Primary);
            Assert.True(palette.ButtonHovered.Y < palette.Raised.Y);
            Assert.True(palette.ButtonActive.Y < palette.ButtonHovered.Y);
        }
    }
    private static double Contrast(Vector4 a, Vector4 b)
    {
        static double Linear(float c) => c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4);
        static double Luminance(Vector4 c) => .2126 * Linear(c.X) + .7152 * Linear(c.Y) + .0722 * Linear(c.Z);
        var x = Luminance(a); var y = Luminance(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
}

