using Exportaramus.Shared.Ui;

namespace Exportaramus.Tests.Main;

public sealed class ThemeBorderFamilyTests
{
    [Fact]
    public void BordersAdaptBetweenLightAndDarkSurfaceFamilies()
    {
        var dark = ThemePolicy.Resolve(0).Border;
        var light = ThemePolicy.Resolve(1).Border;
        Assert.NotEqual(dark, light);
        foreach (uint theme in new uint[] { 0, 2, 3, 5, 6 })
            Assert.Equal(dark, ThemePolicy.Resolve(theme).Border);
        foreach (uint theme in new uint[] { 1, 4, 7 })
            Assert.Equal(light, ThemePolicy.Resolve(theme).Border);
    }
}
