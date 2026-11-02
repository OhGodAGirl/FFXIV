using System.Text;

namespace CollectionShopper.Tests;

public sealed class ShoppingListTextTests
{
    [Theory]
    [InlineData(0, "Outfits", "Outfit")]
    [InlineData(1, "Weapons", "Weapon Type")]
    [InlineData(2, "Minions", "Minion")]
    [InlineData(3, "Mounts", "Mount")]
    [InlineData(4, "Orchestrion Rolls", "Orchestrion Roll")]
    [InlineData(5, "Hairstyles", "Hairstyle")]
    [InlineData(6, "Glasses", "Glasses")]
    [InlineData(7, "Fashion Accessories", "Fashion Accessory")]
    public void EachCategoryHasItsApprovedColumnLabel(int category, string title, string column)
    {
        // Arrange
        var item = ShoppingListPolicyTests.Item(1) with { Category = (ShoppingCategory)category };
        var page = Enum.Parse<ShopperPage>(category switch
        {
            0 => "Outfits", 1 => "Weapons", 2 => "Minions", 3 => "Mounts", 4 => "Orchestrions",
            5 => "Hairstyles", 6 => "Facewear", _ => "Fashion",
        });
        // Act
        var actualTitle = ShoppingListText.GetPageTitle(page);
        var actualColumn = ShoppingListText.GetCollectionColumnTitle([item, item]);
        // Assert
        Assert.Equal(title, actualTitle);
        Assert.Equal(column, actualColumn);
    }

    [Fact]
    public void EmptyMixedAndUnknownCategoriesUseGenericColumn()
    {
        // Arrange
        var item = ShoppingListPolicyTests.Item(1);
        // Act
        var empty = ShoppingListText.GetCollectionColumnTitle([]);
        var mixed = ShoppingListText.GetCollectionColumnTitle([item, item with { Category = ShoppingCategory.Weapon }]);
        var unknown = ShoppingListText.GetCollectionColumnTitle([item with { Category = (ShoppingCategory)999 }]);
        // Assert
        Assert.Equal("Collection", empty);
        Assert.Equal("Collection", mixed);
        Assert.Equal("Collection", unknown);
        Assert.Equal("All Items", ShoppingListText.GetPageTitle(ShopperPage.AllMissing));
        Assert.Equal("All Items", ShoppingListText.GetPageTitle((ShopperPage)999));
    }

    [Theory]
    [InlineData("  青い帽子  \0ignored", "青い帽子")]
    [InlineData("  Coat  ", "Coat")]
    [InlineData("", "")]
    [InlineData("\0ignored", "")]
    public void FilterStopsAtTerminatorAndTrimsUtf8Text(string text, string expected)
    {
        // Arrange
        var buffer = Encoding.UTF8.GetBytes(text);
        // Act
        var result = ShoppingListText.ReadBuffer(buffer);
        // Assert
        Assert.Equal(expected, result);
    }
}
