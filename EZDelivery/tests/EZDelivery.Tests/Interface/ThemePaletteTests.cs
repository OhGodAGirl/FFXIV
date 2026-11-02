using System.Numerics;
using EZDelivery.Features.Interface;
namespace EZDelivery.Tests.Interface;

public sealed class ThemePaletteTests
{
    [Theory]
    [InlineData(0u, .27f, .275f, .27f, false)]
    [InlineData(1u, .94f, .81f, .65f, true)]
    [InlineData(2u, .22f, .10f, .76f, false)]
    [InlineData(3u, .22f, .32f, .55f, false)]
    [InlineData(4u, .78f, .78f, .80f, true)]
    [InlineData(5u, .27f, .46f, .16f, false)]
    [InlineData(6u, .30f, .31f, .32f, false)]
    [InlineData(7u, .93f, .63f, .85f, true)]
    public void ApprovedBasesAndInteractionDirection(uint id, float r, float g, float b, bool light)
    {
        // Arrange
        var expectedSurface = new Vector4(r, g, b, 1);
        var direction = light ? -1f : 1f;

        // Act
        var p = ThemePalette.Resolve(id);

        // Assert
        Assert.Equal(expectedSurface, p.Surface);
        Assert.Equal(expectedSurface + new Vector4(.045f, .045f, .045f, 0) * direction, p.Hover);
        Assert.Equal(expectedSurface + new Vector4(.08f, .08f, .08f, 0) * direction, p.Active);
        Assert.Equal(expectedSurface - new Vector4(.04f, .04f, .04f, 0), p.Inset);
        Assert.Equal(light ? new Vector4(.48f, .40f, .27f, 1) : new Vector4(.72f, .62f, .42f, 1), p.Border);
        Assert.Equal(light, p.Hover.X < p.Surface.X);
        Assert.Equal(light, p.Active.X < p.Hover.X);
        Assert.Equal(1, p.Active.W);
        Assert.Equal(light ? new Vector4(.41f, .28f, .045f, 1) : new Vector4(.91f, .76f, .43f, 1), p.Icon);
        Assert.True(p.Inset.X < p.Surface.X);
    }
    [Fact]
    public void UnknownFallsBackToDark()
    {
        // Arrange
        var dark = ThemePalette.Resolve(0);
        // Act
        var actual = ThemePalette.Resolve(uint.MaxValue);
        // Assert
        Assert.Equal(dark, actual);
    }
}
