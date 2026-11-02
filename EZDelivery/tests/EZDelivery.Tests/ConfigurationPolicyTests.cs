using EZDelivery.Bootstrap;
using EZDelivery.Features.Interface;
using Xunit;

namespace EZDelivery.Tests;

public sealed class ConfigurationPolicyTests
{
    [Fact]
    public void CurrentVersionIsAccepted()
    {
        // Arrange
        const int version = 1;

        // Act
        var error = Record.Exception(() => ConfigurationPolicy.ValidateVersion(version));

        // Assert
        Assert.Null(error);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void UnsupportedVersionFailsWithPreservationMessage(int version)
    {
        // Arrange
        Action validate = () => ConfigurationPolicy.ValidateVersion(version);

        // Act
        var error = Assert.Throws<InvalidOperationException>(validate);

        // Assert
        Assert.Equal("Unsupported EZ Delivery configuration version; original configuration was preserved.", error.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MotionChangesBeforePersistenceAndLeavesStyleUnchanged(bool enabled)
    {
        // Arrange
        var settings = new Settings { ReduceMotion = !enabled, ShopTabStyle = ShopTabStyle.RibbonTabs };
        var saves = 0;
        void Save()
        {
            Assert.Equal(enabled, settings.ReduceMotion);
            saves++;
        }

        // Act
        var handled = ConfigurationPolicy.TryApply(settings, new ReduceMotionIntent(17, enabled), 17, Save, out var error);

        // Assert
        Assert.True(handled);
        Assert.Null(error);
        Assert.Equal(1, saves);
        Assert.Equal(ShopTabStyle.RibbonTabs, settings.ShopTabStyle);
    }

    [Theory]
    [InlineData(ShopTabStyle.InsetSockets)]
    [InlineData(ShopTabStyle.FlushIndex)]
    [InlineData(ShopTabStyle.RibbonTabs)]
    [InlineData(ShopTabStyle.RoundTabs)]
    [InlineData(ShopTabStyle.BeveledIndex)]
    public void EverySupportedStyleIsPersistedWithoutChangingMotion(ShopTabStyle style)
    {
        // Arrange
        var settings = new Settings { ReduceMotion = true };
        var saves = 0;
        void Save()
        {
            Assert.Equal(style, settings.ShopTabStyle);
            saves++;
        }

        // Act
        var handled = ConfigurationPolicy.TryApply(settings, new ShopTabStyleIntent(17, style), 17, Save, out var error);

        // Assert
        Assert.True(handled);
        Assert.Null(error);
        Assert.Equal(1, saves);
        Assert.True(settings.ReduceMotion);
    }

    [Fact]
    public void StaleInvalidAndUnrelatedIntentsCannotMutateOrSaveSettings()
    {
        // Arrange
        var settings = new Settings { ReduceMotion = true, ShopTabStyle = ShopTabStyle.RoundTabs };
        UiIntent[] intents =
        [
            new ReduceMotionIntent(16, false),
            new ShopTabStyleIntent(16, ShopTabStyle.InsetSockets),
            new ShopTabStyleIntent(17, (ShopTabStyle)(-1)),
            new ShopTabStyleIntent(17, (ShopTabStyle)5),
            new StartDeliveryIntent(17),
            new StopDeliveryIntent(17),
            new SelectCategoryIntent(17, ShopCategory.Weapons),
        ];
        var saves = 0;

        // Act / Assert: each rejected intent independently preserves state.
        foreach (var intent in intents)
        {
            var handled = ConfigurationPolicy.TryApply(settings, intent, 17, () => saves++, out var error);
            Assert.False(handled);
            Assert.Null(error);
            Assert.True(settings.ReduceMotion);
            Assert.Equal(ShopTabStyle.RoundTabs, settings.ShopTabStyle);
        }
        Assert.Equal(0, saves);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SaveFailureRetainsRequestedSettingAndReturnsOriginalError(bool motion)
    {
        // Arrange
        var settings = new Settings();
        var failure = new IOException("Disk is unavailable");
        UiIntent intent = motion ? new ReduceMotionIntent(17, true) : new ShopTabStyleIntent(17, ShopTabStyle.RoundTabs);
        var saves = 0;
        void Save()
        {
            saves++;
            throw failure;
        }

        // Act
        var handled = ConfigurationPolicy.TryApply(settings, intent, 17, Save, out var error);

        // Assert
        Assert.True(handled);
        Assert.Same(failure, error);
        Assert.Equal(1, saves);
        Assert.Equal(motion, settings.ReduceMotion);
        Assert.Equal(motion ? ShopTabStyle.InsetSockets : ShopTabStyle.RoundTabs, settings.ShopTabStyle);
    }

    private sealed class Settings : IConfigurationSettings
    {
        public bool ReduceMotion { get; set; }
        public ShopTabStyle ShopTabStyle { get; set; } = ShopTabStyle.InsetSockets;
    }
}
