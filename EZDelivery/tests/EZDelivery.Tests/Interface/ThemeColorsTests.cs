using System.Numerics;
using EZDelivery.Features.Interface;

namespace EZDelivery.Tests.Interface;

public sealed class ThemeColorsTests
{
    [Theory]
    [InlineData(0u)] [InlineData(1u)] [InlineData(2u)] [InlineData(3u)]
    [InlineData(4u)] [InlineData(5u)] [InlineData(6u)] [InlineData(7u)]
    public void EveryWindowRoleIsFiniteAndPreservesItsAlphaAcrossThemes(uint theme)
    {
        // Arrange
        var roles = Enum.GetValues<ThemeColorRole>();
        Assert.Equal(31, roles.Length);

        foreach (var role in roles)
        {
            var dark = ThemeColors.Resolve(0, role);
            // Act
            var actual = ThemeColors.Resolve(theme, role);
            // Assert
            Assert.InRange(actual.X, 0, 1); Assert.InRange(actual.Y, 0, 1); Assert.InRange(actual.Z, 0, 1);
            Assert.Equal(dark.W, actual.W);
        }
    }

    [Theory]
    [InlineData(1u)] [InlineData(4u)] [InlineData(7u)]
    public void LightThemeTextAndDisabledTextRemainDarkOverWindowAndPopup(uint theme)
    {
        // Arrange
        var expectedText = new Vector4(.2f,.16f,.2f,1);
        // Act
        var text = ThemeColors.Resolve(theme, ThemeColorRole.Text);
        var disabled = ThemeColors.Resolve(theme, ThemeColorRole.TextDisabled);
        var popup = ThemeColors.Resolve(theme, ThemeColorRole.PopupBg);
        var window = ThemeColors.Resolve(theme, ThemeColorRole.WindowBg);
        // Assert
        Assert.Equal(expectedText, text); Assert.Equal(expectedText, disabled);
        Assert.True(popup.X - text.X > .5f); Assert.True(window.X - text.X > .5f);
    }

    [Fact]
    public void UnknownRoleIsRejectedRatherThanSilentlyMiscolored()
    {
        // Arrange
        var role = (ThemeColorRole)999;
        // Act
        var error = Record.Exception(() => ThemeColors.Resolve(0, role));
        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(error);
    }
}
