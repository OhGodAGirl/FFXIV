namespace CollectionShopper.Tests;

public sealed class CatalogEligibilityPolicyTests
{
    [Theory]
    [InlineData(false, false, 1u, false)]
    [InlineData(true, true, 1u, false)]
    [InlineData(true, false, 0u, false)]
    [InlineData(true, false, 1u, true)]
    public void MarketAdmissionRequiresExistingTradableSearchableItem(bool exists, bool untradable, uint category, bool expected)
    {
        // Arrange / Act
        var result = CatalogEligibilityPolicy.IsMarketEligible(exists, untradable, category);
        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0u, 1, 1u, true, false, false, 1u, false)]
    [InlineData(1u, 2, 1u, true, false, false, 1u, false)]
    [InlineData(1u, 1, 1u, false, false, false, 1u, false)]
    [InlineData(1u, 1, 1u, true, false, true, 1u, false)]
    [InlineData(1u, 1, 1u, true, false, false, 0u, false)]
    [InlineData(1u, 1, 1u, true, false, false, 1u, true)]
    [InlineData(1u, 1, 1u, false, true, false, 1u, true)]
    [InlineData(1u, 2, 11u, true, false, false, 1u, true)]
    public void WeaponsRequireCombatOrShieldAndStorageEligibility(uint id, int major, uint category,
        bool glamorous, bool armoire, bool untradable, uint search, bool expected)
    {
        // Arrange / Act
        var result = CatalogEligibilityPolicy.IsWeaponEligible(id, major, category, glamorous, armoire, untradable, search);
        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData("ui/icon.tex", "ui/icon.tex")]
    [InlineData("UI/icon.tex", "UI/icon.tex")]
    [InlineData("relative", null)]
    [InlineData("https://example.test/icon?other=1&path=ui%2Ficon.tex", "ui/icon.tex")]
    [InlineData("https://example.test/icon?PATH=UI%2Ficon.tex", "UI/icon.tex")]
    [InlineData("https://example.test/icon?path=other", null)]
    [InlineData("https://example.test/icon?path&other=ui%2Ficon.tex", null)]
    [InlineData("https://example.test/icon", null)]
    [InlineData("https://example.test/icon?path=invalid&path=ui%2Ficon.tex", null)]
    public void AssetMappingRetainsOnlyTheExistingNativePathContract(string? input, string? expected)
    {
        // Arrange / Act
        var result = CatalogEligibilityPolicy.GameAssetPath(input);
        // Assert
        Assert.Equal(expected, result);
    }
}
