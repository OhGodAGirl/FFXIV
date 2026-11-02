namespace CollectionShopper;

internal static class ShopperPagePolicy
{
    internal static ShoppingCategory? GetCategory(ShopperPage page)
        => page switch
        {
            ShopperPage.Outfits => ShoppingCategory.Outfit,
            ShopperPage.Weapons => ShoppingCategory.Weapon,
            ShopperPage.Minions => ShoppingCategory.Minion,
            ShopperPage.Mounts => ShoppingCategory.Mount,
            ShopperPage.Orchestrions => ShoppingCategory.Orchestrion,
            ShopperPage.Hairstyles => ShoppingCategory.Hairstyle,
            ShopperPage.Facewear => ShoppingCategory.Facewear,
            ShopperPage.Fashion => ShoppingCategory.Fashion,
            _ => null,
        };

    internal static ShopperPage KeepVisible(
        ShopperPage activePage,
        Func<ShoppingCategory, bool> isCategoryTabVisible)
    {
        var category = GetCategory(activePage);
        return category is null || isCategoryTabVisible(category.Value)
            ? activePage
            : ShopperPage.AllMissing;
    }

    internal static bool IncludesPurchaseCategory(
        ShopperPage page,
        ShoppingCategory category,
        Func<ShoppingCategory, bool> isAutomatedCategoryEnabled)
    {
        var pageCategory = GetCategory(page);
        if (pageCategory is not null)
            return pageCategory.Value == category;

        return page == ShopperPage.AllMissing && isAutomatedCategoryEnabled(category);
    }

}
