namespace CollectionShopper.Tests;

public sealed class CategorySettingsPolicyTests
{
    [Theory]
    [InlineData(0, "ShowOutfitsTab", "PurchaseAllMissingOutfits", "Outfits")]
    [InlineData(1, "ShowWeaponsTab", "PurchaseAllMissingWeapons", "Weapons")]
    [InlineData(2, "ShowMinionsTab", "PurchaseAllMissingMinions", "Minions")]
    [InlineData(3, "ShowMountsTab", "PurchaseAllMissingMounts", "Mounts")]
    [InlineData(4, "ShowOrchestrionsTab", "PurchaseAllMissingOrchestrions", "Orchestrion Rolls")]
    [InlineData(5, "ShowHairstylesTab", "PurchaseAllMissingHairstyles", "Hairstyles")]
    [InlineData(6, "ShowFacewearTab", "PurchaseAllMissingFacewear", "Glasses")]
    [InlineData(7, "ShowFashionTab", "PurchaseAllMissingFashion", "Fashion Accessories")]
    public void CategoryControlsChangeOnlyTheirMatchingPersistedFields(int categoryId,
        string tabProperty, string purchaseProperty, string label)
    {
        // Arrange
        var configuration = new Configuration();
        var category = (ShoppingCategory)categoryId;
        var before = System.Text.Json.JsonSerializer.Serialize(configuration);
        // Act
        CategorySettingsPolicy.SetCategoryTabVisible(configuration, category, false);
        CategorySettingsPolicy.SetAutomaticCategoryEnabled(configuration, category, true);
        // Assert
        Assert.False(CategorySettingsPolicy.IsCategoryTabVisible(configuration, category));
        Assert.True(CategorySettingsPolicy.IsAutomaticCategoryEnabled(configuration, category));
        Assert.Equal(label, CategorySettingsPolicy.GetCategoryLabel(category));
        foreach (var property in typeof(Configuration).GetProperties().Where(p => p.PropertyType == typeof(bool)))
        {
            var expected = property.Name == purchaseProperty || (property.Name != tabProperty
                && (bool)property.GetValue(new Configuration())!);
            Assert.Equal(expected, property.GetValue(configuration));
        }
        // Act: toggling back must restore the serialized configuration exactly.
        CategorySettingsPolicy.SetCategoryTabVisible(configuration, category, true);
        CategorySettingsPolicy.SetAutomaticCategoryEnabled(configuration, category, false);
        // Assert
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(configuration));
        Assert.True(CategorySettingsPolicy.IsCategoryTabVisible(configuration, category));
        Assert.False(CategorySettingsPolicy.IsAutomaticCategoryEnabled(configuration, category));
    }

    [Fact]
    public void UnknownCategoryPreservesConfigurationAndUsesExistingFallbacks()
    {
        // Arrange
        var configuration = new Configuration();
        var category = (ShoppingCategory)999;
        var before = System.Text.Json.JsonSerializer.Serialize(configuration);
        // Act
        CategorySettingsPolicy.SetCategoryTabVisible(configuration, category, false);
        CategorySettingsPolicy.SetAutomaticCategoryEnabled(configuration, category, true);
        // Assert
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(configuration));
        Assert.True(CategorySettingsPolicy.IsCategoryTabVisible(configuration, category));
        Assert.False(CategorySettingsPolicy.IsAutomaticCategoryEnabled(configuration, category));
        Assert.Equal("Items", CategorySettingsPolicy.GetCategoryLabel(category));
    }
}
