using EZDelivery.Bootstrap;
using EZDelivery.Features.Interface;
using Newtonsoft.Json;
using Xunit;

namespace EZDelivery.HostTests;

public sealed class ConfigurationSerializationTests
{
    [Theory]
    [InlineData(ShopTabStyle.InsetSockets, false)]
    [InlineData(ShopTabStyle.FlushIndex, true)]
    [InlineData(ShopTabStyle.RibbonTabs, false)]
    [InlineData(ShopTabStyle.RoundTabs, true)]
    [InlineData(ShopTabStyle.BeveledIndex, false)]
    public void ActualConfigurationRoundTripsEveryPersistedSetting(ShopTabStyle style, bool reduceMotion)
    {
        // Arrange
        var configuration = new Configuration { ShopTabStyle = style, ReduceMotion = reduceMotion };

        // Act
        var serialized = JsonConvert.SerializeObject(configuration);
        var restored = JsonConvert.DeserializeObject<Configuration>(serialized)!;

        // Assert
        Assert.NotSame(configuration, restored);
        Assert.Equal(ConfigurationPolicy.CurrentVersion, restored.Version);
        Assert.Equal(style, restored.ShopTabStyle);
        Assert.Equal(reduceMotion, restored.ReduceMotion);
    }

    [Theory]
    [InlineData("{}", false)]
    [InlineData("{\"Version\":1,\"ReduceMotion\":true}", true)]
    public void OlderConfigurationWithoutTabStyleRetainsCompatibleDefault(string json, bool reduceMotion)
    {
        // Arrange / Act
        var restored = JsonConvert.DeserializeObject<Configuration>(json)!;

        // Assert
        Assert.Equal(ConfigurationPolicy.CurrentVersion, restored.Version);
        Assert.Equal(ShopTabStyle.InsetSockets, restored.ShopTabStyle);
        Assert.Equal(reduceMotion, restored.ReduceMotion);
    }

    [Fact]
    public void DeserializationDoesNotSilentlyRewriteUnsupportedVersionOrUnknownStyle()
    {
        // Arrange
        const string json = "{\"Version\":99,\"ShopTabStyle\":999}";

        // Act
        var restored = JsonConvert.DeserializeObject<Configuration>(json)!;

        // Assert: validation, not deserialization, owns rejection/fallback.
        Assert.Equal(99, restored.Version);
        Assert.Equal((ShopTabStyle)999, restored.ShopTabStyle);
        Assert.Equal(ShopTabStyle.InsetSockets, TabPresentation.Normalize(restored.ShopTabStyle));
    }
}
