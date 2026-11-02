using System.Numerics;
using System.Text.Json;

namespace SpringCleaning.Tests;

public sealed class ConfigurationAndThemeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyProtectionPreferenceIsInheritedAndOverridesStayIndependent(bool legacy)
    {
        var config = new Configuration { ProtectMateriaAndFullySpiritboundItems = legacy };
        Assert.Equal(legacy, config.ProtectItemsWithMateria);
        Assert.Equal(legacy, config.ProtectItemsWithSpiritbond);
        config.ProtectItemsWithMateria = !legacy;
        Assert.Equal(!legacy, config.ProtectItemsWithMateria);
        Assert.Equal(legacy, config.ProtectItemsWithSpiritbond);
        config.ProtectItemsWithSpiritbond = !legacy;
        config.ProtectMateriaAndFullySpiritboundItems = legacy;
        Assert.Equal(!legacy, config.ProtectItemsWithMateria);
        Assert.Equal(!legacy, config.ProtectItemsWithSpiritbond);
    }

    [Theory]
    [InlineData("{\"ProtectItemsWithMateria\":false,\"ProtectItemsWithSpiritbond\":true,\"ProtectMateriaAndFullySpiritboundItems\":false}")]
    [InlineData("{\"ProtectMateriaAndFullySpiritboundItems\":false,\"ProtectItemsWithSpiritbond\":true,\"ProtectItemsWithMateria\":false}")]
    public void ExplicitProtectionKeysWinRegardlessOfDeserializationOrder(string json)
    {
        var config = JsonSerializer.Deserialize<Configuration>(json)!;
        Assert.False(config.ProtectItemsWithMateria);
        Assert.True(config.ProtectItemsWithSpiritbond);
    }

    [Fact]
    public void NewSettingsDefaultSafelyAndPersistEveryTabStyleAndIgnoredItem()
    {
        var config = JsonSerializer.Deserialize<Configuration>("{}")!;
        Assert.Equal(1, config.Version);
        Assert.Equal(750, config.ActionDelayMilliseconds);
        Assert.Equal(8000, config.ConfirmationTimeoutMilliseconds);
        Assert.Equal(2f, config.InteractionDistance);
        Assert.Equal(BookmarkTabStyle.RoundTabs, config.TabStyle);
        Assert.True(config.ProtectItemsWithMateria);
        Assert.True(config.ProtectItemsWithSpiritbond);
        Assert.Empty(config.IgnoredDuplicateItemIds);
        foreach (var style in Enum.GetValues<BookmarkTabStyle>())
        {
            config.TabStyle = style;
            config.IgnoredDuplicateItemIds = [2924, 3233];
            var restored = JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(config))!;
            Assert.Equal(style, restored.TabStyle);
            Assert.True(restored.IgnoredDuplicateItemIds.SetEquals([2924u, 3233u]));
        }
    }

    [Theory]
    [InlineData(0u, .27f, .275f, .27f, false)]
    [InlineData(1u, .94f, .81f, .65f, true)]
    [InlineData(2u, .22f, .10f, .76f, false)]
    [InlineData(3u, .22f, .32f, .55f, false)]
    [InlineData(4u, .78f, .78f, .80f, true)]
    [InlineData(5u, .27f, .46f, .16f, false)]
    [InlineData(6u, .30f, .31f, .32f, false)]
    [InlineData(7u, .93f, .63f, .85f, true)]
    [InlineData(99u, .27f, .275f, .27f, false)]
    public void ThemeMatchesApprovedPalette(uint id, float red, float green, float blue, bool light)
    {
        var state = new ArmoireThemeState();
        state.Update(0, () => id);
        Assert.Equal(new Vector4(red, green, blue, 1), state.Surface);
        Assert.Equal(light, state.Light);
    }

    [Fact]
    public void ThemeReadIsThrottledAtExactBoundaryAndFailuresRetainPriorTheme()
    {
        var state = new ArmoireThemeState();
        Assert.Equal(new Vector4(.27f, .275f, .27f, 1), state.Surface);
        Assert.False(state.Light);
        var reads = 0;
        uint? Read() { reads++; return 1; }
        state.Update(100, Read);
        var surface = state.Surface;
        state.Update(1099, Read);
        Assert.Equal(1, reads);
        state.Update(1100, Read);
        Assert.Equal(2, reads);
        state.Update(2100, () => null);
        Assert.Equal(surface, state.Surface);
        Assert.True(state.Light);
        state.Update(3100, () => throw new InvalidOperationException());
        Assert.Equal(surface, state.Surface);
        Assert.True(state.Light);
        state.Update(4099, Read);
        Assert.Equal(2, reads);
        state.Update(4100, () => 2);
        Assert.False(state.Light);
        Assert.Equal(new Vector4(.22f, .10f, .76f, 1), state.Surface);
    }
}
