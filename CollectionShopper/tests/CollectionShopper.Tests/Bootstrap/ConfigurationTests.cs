using System.Text.Json;

namespace CollectionShopper.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void NewConfigurationPreservesAcceptedDefaults()
    {
        // Arrange / Act
        var config = new Configuration();

        // Assert
        Assert.Equal(3, config.Version);
        Assert.Equal(600, config.ActionDelayMilliseconds);
        Assert.Equal(MarketBoardTabPresentation.TabStyle.RoundTabs, config.MarketBoardTabStyle);
        Assert.True(config.HideLocallyOwned);
        Assert.True(config.HideCompletedPurchases);
        Assert.True(config.AutomaticallyRefreshCollection);
        Assert.True(config.ShowOutfitsTab);
        Assert.True(config.ShowWeaponsTab);
        Assert.True(config.ShowMinionsTab);
        Assert.True(config.ShowMountsTab);
        Assert.True(config.ShowOrchestrionsTab);
        Assert.True(config.ShowHairstylesTab);
        Assert.True(config.ShowFacewearTab);
        Assert.True(config.ShowFashionTab);
        Assert.False(config.UnlimitedPricePerItem);
        Assert.False(config.UnlimitedTotalSpend);
        Assert.False(config.EnableHighQualityPurchases);
        Assert.False(config.ReduceMotion);
        Assert.False(config.PurchaseAllMissingOutfits);
        Assert.False(config.PurchaseAllMissingWeapons);
        Assert.False(config.PurchaseAllMissingMinions);
        Assert.False(config.PurchaseAllMissingMounts);
        Assert.False(config.PurchaseAllMissingOrchestrions);
        Assert.False(config.PurchaseAllMissingHairstyles);
        Assert.False(config.PurchaseAllMissingFacewear);
        Assert.False(config.PurchaseAllMissingFashion);
        Assert.Equal(0, config.MaximumPricePerItem);
        Assert.Equal(0, config.MaximumTotalSpend);
        Assert.Empty(config.CompletedItemIds);
        Assert.Empty(config.PurchaseTracking);
    }

    [Fact]
    public void ConfigurationRoundTripPreservesSettingsAndCharacterPurchaseEvidence()
    {
        // Arrange
        var now = ExportaramusFixture.Now;
        var config = new Configuration {
            Version = 2, ActionDelayMilliseconds = 777, MarketBoardTabStyle = MarketBoardTabPresentation.TabStyle.BeveledIndex,
            HideLocallyOwned = false, HideCompletedPurchases = false, AutomaticallyRefreshCollection = false,
            ShowOutfitsTab = false, ShowWeaponsTab = false, ShowMinionsTab = false, ShowMountsTab = false,
            ShowOrchestrionsTab = false, ShowHairstylesTab = false, ShowFacewearTab = false, ShowFashionTab = false,
            UnlimitedPricePerItem = true, UnlimitedTotalSpend = true, MaximumPricePerItem = 123, MaximumTotalSpend = 456,
            EnableHighQualityPurchases = true, ReduceMotion = true, PurchaseAllMissingOutfits = true,
            PurchaseAllMissingWeapons = true, PurchaseAllMissingMinions = true, PurchaseAllMissingMounts = true,
            PurchaseAllMissingOrchestrions = true, PurchaseAllMissingHairstyles = true,
            PurchaseAllMissingFacewear = true, PurchaseAllMissingFashion = true,
            CompletedItemIds = [1, 2], PurchaseTracking = [new(42, 1, now), new(43, 2, now, now.AddSeconds(1))],
        };

        // Act
        var json = JsonSerializer.Serialize(config);
        var restored = JsonSerializer.Deserialize<Configuration>(json)!;

        // Assert
        Assert.Equal(json, JsonSerializer.Serialize(restored));
        Assert.Equal(config.PurchaseTracking, restored.PurchaseTracking);
        Assert.True(restored.CompletedItemIds.SetEquals([1, 2]));
        Assert.Equal(2, restored.Version);
        Assert.Equal(777, restored.ActionDelayMilliseconds);
        Assert.NotSame(config.PurchaseTracking, restored.PurchaseTracking);
        Assert.NotSame(config.CompletedItemIds, restored.CompletedItemIds);
    }

    [Theory]
    [InlineData(-1, 1250, 600, true)]
    [InlineData(0, 1250, 600, true)]
    [InlineData(1, 1250, 600, true)]
    [InlineData(1, 777, 777, true)]
    [InlineData(2, 1250, 1250, true)]
    [InlineData(2, 600, 600, true)]
    [InlineData(3, 1250, 1250, false)]
    [InlineData(4, 1250, 1250, false)]
    public void MigrationOnlyChangesLegacyDefaultDelayAndOlderVersions(int version, int delay, int expectedDelay, bool expectedChange)
    {
        // Arrange / Act
        var migration = ConfigurationMigrationPolicy.Evaluate(version, delay);

        // Assert
        Assert.Equal((expectedChange ? 3 : version, expectedDelay, expectedChange), migration);
    }
}
