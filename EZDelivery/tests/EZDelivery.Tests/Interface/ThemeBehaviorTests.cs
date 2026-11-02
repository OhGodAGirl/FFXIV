using System.Numerics;
using EZDelivery.Features.Interface;

namespace EZDelivery.Tests.Interface;

public sealed class ThemeBehaviorTests
{
    [Theory]
    [InlineData(.6f, .7f, .7f, .5433333f, .4133333f, .2533333f)]
    [InlineData(.7f, .6f, .7f, .5433333f, .4133333f, .2533333f)]
    [InlineData(.7f, .5f, .7f, .5766667f, .4466667f, .2866667f)]
    [InlineData(.5f, .7f, .7f, .5766667f, .4466667f, .2866667f)]
    [InlineData(.39f, .3f, .2f, .9133333f, .7833333f, .6233333f)]
    [InlineData(.5f, .3f, .4f, .81f, .68f, .52f)]
    [InlineData(.65f, .3f, .5f, .7266667f, .5966667f, .4366667f)]
    [InlineData(.4f, .3f, .2f, .91f, .78f, .62f)]
    [InlineData(.5f, .3f, .5f, .7766667f, .6466667f, .4866667f)]
    public void ThresholdBoundaryColorsKeepSurfaceBrightnessInsteadOfBecomingTextOrGold(
        float r, float g, float b, float expectedR, float expectedG, float expectedB)
    {
        var actual = ThemePalette.Transform(1, new(r, g, b, .43f));
        Assert.Equal(expectedR, actual.X, 5);
        Assert.Equal(expectedG, actual.Y, 5);
        Assert.Equal(expectedB, actual.Z, 5);
        Assert.Equal(.43f, actual.W);
    }

    [Fact]
    public void PollingRetainsLastSuccessThroughFailuresAndThrottlesAtExactBoundary()
    {
        // Arrange
        var observer = new ThemeObserver();
        var reads = 0;
        uint? Read() { reads++; return 4; }
        Assert.Equal(0u, observer.Current);

        // Act
        observer.Update(0, Read);
        observer.Update(499, Read);

        // Assert
        Assert.Equal(1, reads);
        Assert.Equal(4u, observer.Current);

        // Act: the exact deadline permits another read; a missing setting keeps White.
        observer.Update(500, () => { reads++; return null; });
        observer.Update(999, Read);

        // Assert
        Assert.Equal(2, reads);
        Assert.Equal(4u, observer.Current);

        // Act: exceptions are also retained, then a future successful read recovers.
        observer.Update(1000, () => throw new InvalidOperationException());
        Assert.Equal(4u, observer.Current);
        observer.Update(1500, () => 7);
        Assert.Equal(7u, observer.Current);
        observer.Update(2000, () => 99);
        Assert.Equal(0u, observer.Current);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(99u)]
    public void DarkAndUnknownPreserveExistingSemanticColor(uint theme)
    {
        // Arrange
        var original = new Vector4(.8f, .7f, .5f, .55f);
        // Act
        var actual = ThemePalette.Transform(theme, original);
        // Assert
        Assert.Equal(original, actual);
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(4u)]
    [InlineData(7u)]
    public void LightThemesUseDarkReadableTextAndKeepTransparency(uint theme)
    {
        // Arrange
        var original = new Vector4(.9f, .89f, .85f, .55f);
        // Act
        var actual = ThemePalette.Transform(theme, original);
        // Assert
        Assert.Equal(new Vector4(.2f, .16f, .2f, .55f), actual);
    }

    [Theory]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(5u)]
    [InlineData(6u)]
    public void DarkFamiliesRetainLightForeground(uint theme)
    {
        // Arrange
        var original = new Vector4(.9f, .89f, .85f, .7f);
        // Act
        var actual = ThemePalette.Transform(theme, original);
        // Assert
        Assert.Equal(original, actual);
    }

    [Theory]
    [InlineData(1u, .41f, .28f, .045f)]
    [InlineData(2u, .91f, .76f, .43f)]
    public void AccentUsesApprovedIconColor(uint theme, float r, float g, float b)
    {
        // Arrange
        var original = new Vector4(.82f, .70f, .43f, .6f);
        // Act
        var actual = ThemePalette.Transform(theme, original);
        // Assert
        Assert.Equal(new Vector4(r, g, b, .6f), actual);
    }

    [Theory]
    [InlineData(1u, .91f, .78f, .62f)]
    [InlineData(2u, .25f, .13f, .79f)]
    public void SurfaceOffsetInvertsForLightThemes(uint theme, float r, float g, float b)
    {
        // Arrange
        var original = new Vector4(.3f, .3f, .3f, .9f);
        // Act
        var actual = ThemePalette.Transform(theme, original);
        // Assert
        Assert.Equal(r, actual.X, 5); Assert.Equal(g, actual.Y, 5); Assert.Equal(b, actual.Z, 5);
        Assert.Equal(.9f, actual.W);
    }

    [Theory]
    [InlineData(.7f, .5f, .6f)]
    [InlineData(.7f, .7f, .4f)]
    [InlineData(.4f, .3f, .2f)]
    [InlineData(-2f, -2f, -2f)]
    [InlineData(2f, 2f, .5f)]
    public void NonTextInputsProduceBoundedColors(float r, float g, float b)
    {
        // Arrange
        var original = new Vector4(r, g, b, 1);
        // Act
        var actual = ThemePalette.Transform(3, original);
        // Assert
        Assert.InRange(actual.X, 0, 1); Assert.InRange(actual.Y, 0, 1); Assert.InRange(actual.Z, 0, 1);
        Assert.Equal(1, actual.W);
    }
}
