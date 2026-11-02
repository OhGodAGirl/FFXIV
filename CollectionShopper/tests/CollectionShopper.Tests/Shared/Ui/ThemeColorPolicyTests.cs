using System.Numerics;

namespace CollectionShopper.Tests;

public sealed class ThemeColorPolicyTests
{
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(4u)]
    [InlineData(5u)]
    [InlineData(6u)]
    [InlineData(7u)]
    public void KnownThemeIdentifiersArePreserved(uint theme)
        => Assert.Equal(theme, ThemeColorPolicy.Normalize(theme));

    [Theory]
    [InlineData(8u)]
    [InlineData(uint.MaxValue)]
    public void UnknownThemeIdentifiersUseDark(uint theme)
        => Assert.Equal(0u, ThemeColorPolicy.Normalize(theme));

    [Fact]
    public void DarkPreservesOriginalColorsIncludingTransparency()
    {
        var original = new Vector4(.18f, .182f, .185f, .99f);
        Assert.Equal(original, ThemeColorPolicy.Apply(0, original));
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(4u)]
    [InlineData(7u)]
    public void LightFamiliesUseDarkTextAndBrownAccents(uint theme)
    {
        Assert.Equal(new Vector4(.20f, .16f, .20f, .75f), ThemeColorPolicy.Apply(theme, new(.9f, .89f, .85f, .75f)));
        Assert.Equal(new Vector4(.41f, .28f, .045f, .6f), ThemeColorPolicy.Apply(theme, new(.82f, .70f, .43f, .6f)));
    }

    [Theory]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(5u)]
    [InlineData(6u)]
    public void DarkFamiliesPreserveLightTextAndUseGoldAccents(uint theme)
    {
        var text = new Vector4(.9f, .89f, .85f, .75f);
        Assert.Equal(text, ThemeColorPolicy.Apply(theme, text));
        Assert.Equal(new Vector4(.91f, .76f, .43f, .6f), ThemeColorPolicy.Apply(theme, new(.82f, .70f, .43f, .6f)));
    }

    [Theory]
    [InlineData(3u, .25f, .35f, .58f)]
    [InlineData(7u, .90f, .60f, .82f)]
    public void SurfaceBrightnessAndTransparencyFollowTheSelectedFamily(uint theme, float r, float g, float b)
    {
        AssertVector(new(r, g, b, .42f), ThemeColorPolicy.Apply(theme, new(.30f, .30f, .30f, .42f)));
    }

    [Theory]
    [InlineData(.6f, .7f, .7f)]
    [InlineData(.7f, .6f, .7f)]
    [InlineData(.61f, .7f, .5f)]
    [InlineData(.4f, .2f, .1f)]
    [InlineData(.52f, .2f, .4f)]
    public void TextAndAccentThresholdEqualityRemainsASurface(float r, float g, float b)
    {
        var meanOffset = (r + g + b) / 3f - .27f;
        AssertVector(new(.93f - meanOffset, .63f - meanOffset, .85f - meanOffset, .8f),
            ThemeColorPolicy.Apply(7, new(r, g, b, .8f)));
    }

    [Fact]
    public void SurfaceChannelsClampWithoutChangingAlpha()
    {
        Assert.Equal(new Vector4(0, 0, 0, .25f), ThemeColorPolicy.Apply(3, new(-1, -1, -1, .25f)));
        Assert.Equal(new Vector4(1, 1, 1, .25f), ThemeColorPolicy.Apply(7, new(-1, -1, -1, .25f)));
        Assert.Equal(new Vector4(1, 1, 1, .25f), ThemeColorPolicy.Apply(3, new(.1f, 5, 5, .25f)));
        Assert.Equal(new Vector4(0, 0, 0, .25f), ThemeColorPolicy.Apply(7, new(.1f, 5, 5, .25f)));
    }

    private static void AssertVector(Vector4 expected, Vector4 actual)
    {
        Assert.Equal(expected.X, actual.X, 5);
        Assert.Equal(expected.Y, actual.Y, 5);
        Assert.Equal(expected.Z, actual.Z, 5);
        Assert.Equal(expected.W, actual.W);
    }
}
