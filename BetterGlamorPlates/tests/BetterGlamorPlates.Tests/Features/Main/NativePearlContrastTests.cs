using System.Numerics;
using BetterGlamorPlates.Shared.Ui;

namespace BetterGlamorPlates.Tests.Features.Main;

/// <summary>Checks readable foregrounds across Native Pearl light and dark surfaces.</summary>
public sealed class NativePearlContrastTests
{
    /// <summary>Light surfaces use charcoal text, including selected and pressed controls.</summary>
    [Fact]
    public void PearlSurfacesHaveReadableText()
    {
        foreach (var surface in new[] { PluginTheme.WindowBackground, PluginTheme.PearlMint,
            PluginTheme.FrameIvory, PluginTheme.Selection, PluginTheme.PrimaryAction,
            PluginTheme.PrimaryActionHovered, PluginTheme.PrimaryActionActive })
            Assert.True(Contrast(surface, PluginTheme.PearlText) >= 4.5);
        Assert.True(Contrast(PluginTheme.PearlMint, PluginTheme.PearlWarning) >= 4.5);
    }

    /// <summary>Charcoal wells and controls retain contrasting ivory labels.</summary>
    [Fact]
    public void CharcoalSurfacesHaveReadableText()
    {
        Assert.True(Contrast(PluginTheme.PanelBackground, PluginTheme.Text) >= 4.5);
        Assert.True(Contrast(PluginTheme.FrameBackground, PluginTheme.Text) >= 4.5);
    }

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
