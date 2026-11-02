namespace CollectionShopper;

internal static class ShoppingListText
{
    internal static string GetPageTitle(ShopperPage page)
        => page switch
        {
            ShopperPage.Outfits => "Outfits",
            ShopperPage.Weapons => "Weapons",
            ShopperPage.Minions => "Minions",
            ShopperPage.Mounts => "Mounts",
            ShopperPage.Orchestrions => "Orchestrion Rolls",
            ShopperPage.Hairstyles => "Hairstyles",
            ShopperPage.Facewear => "Glasses",
            ShopperPage.Fashion => "Fashion Accessories",
            _ => "All Items",
        };

    internal static string GetCollectionColumnTitle(ShoppingItem[] panelItems)
    {
        if (panelItems.Length == 0)
            return "Collection";

        var category = panelItems[0].Category;
        for (var index = 1; index < panelItems.Length; index++)
        {
            if (panelItems[index].Category != category)
                return "Collection";
        }

        return category switch
        {
            ShoppingCategory.Outfit => "Outfit",
            ShoppingCategory.Weapon => "Weapon Type",
            ShoppingCategory.Minion => "Minion",
            ShoppingCategory.Mount => "Mount",
            ShoppingCategory.Orchestrion => "Orchestrion Roll",
            ShoppingCategory.Hairstyle => "Hairstyle",
            ShoppingCategory.Facewear => "Glasses",
            ShoppingCategory.Fashion => "Fashion Accessory",
            _ => "Collection",
        };
    }


    internal static string ReadBuffer(byte[] buffer)
    {
        var length = Array.IndexOf(buffer, (byte)0);
        return System.Text.Encoding.UTF8.GetString(buffer, 0, length < 0 ? buffer.Length : length).Trim();
    }
}
