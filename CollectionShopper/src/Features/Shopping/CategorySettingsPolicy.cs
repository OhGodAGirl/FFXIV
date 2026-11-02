namespace CollectionShopper;

internal static class CategorySettingsPolicy
{
    internal static bool IsAutomaticCategoryEnabled(Configuration configuration, ShoppingCategory category)
        => category switch
        {
            ShoppingCategory.Outfit => configuration.PurchaseAllMissingOutfits,
            ShoppingCategory.Weapon => configuration.PurchaseAllMissingWeapons,
            ShoppingCategory.Minion => configuration.PurchaseAllMissingMinions,
            ShoppingCategory.Mount => configuration.PurchaseAllMissingMounts,
            ShoppingCategory.Orchestrion => configuration.PurchaseAllMissingOrchestrions,
            ShoppingCategory.Hairstyle => configuration.PurchaseAllMissingHairstyles,
            ShoppingCategory.Facewear => configuration.PurchaseAllMissingFacewear,
            ShoppingCategory.Fashion => configuration.PurchaseAllMissingFashion,
            _ => false,
        };

    internal static bool IsCategoryTabVisible(Configuration configuration, ShoppingCategory category)
        => category switch
        {
            ShoppingCategory.Outfit => configuration.ShowOutfitsTab,
            ShoppingCategory.Weapon => configuration.ShowWeaponsTab,
            ShoppingCategory.Minion => configuration.ShowMinionsTab,
            ShoppingCategory.Mount => configuration.ShowMountsTab,
            ShoppingCategory.Orchestrion => configuration.ShowOrchestrionsTab,
            ShoppingCategory.Hairstyle => configuration.ShowHairstylesTab,
            ShoppingCategory.Facewear => configuration.ShowFacewearTab,
            ShoppingCategory.Fashion => configuration.ShowFashionTab,
            _ => true,
        };

    internal static string GetCategoryLabel(ShoppingCategory category)
        => category switch
        {
            ShoppingCategory.Outfit => "Outfits",
            ShoppingCategory.Weapon => "Weapons",
            ShoppingCategory.Minion => "Minions",
            ShoppingCategory.Mount => "Mounts",
            ShoppingCategory.Orchestrion => "Orchestrion Rolls",
            ShoppingCategory.Hairstyle => "Hairstyles",
            ShoppingCategory.Facewear => "Glasses",
            ShoppingCategory.Fashion => "Fashion Accessories",
            _ => "Items",
        };

    internal static void SetAutomaticCategoryEnabled(Configuration configuration, ShoppingCategory category, bool enabled)
    {
        switch (category)
        {
            case ShoppingCategory.Outfit:
                configuration.PurchaseAllMissingOutfits = enabled;
                break;
            case ShoppingCategory.Weapon:
                configuration.PurchaseAllMissingWeapons = enabled;
                break;
            case ShoppingCategory.Minion:
                configuration.PurchaseAllMissingMinions = enabled;
                break;
            case ShoppingCategory.Mount:
                configuration.PurchaseAllMissingMounts = enabled;
                break;
            case ShoppingCategory.Orchestrion:
                configuration.PurchaseAllMissingOrchestrions = enabled;
                break;
            case ShoppingCategory.Hairstyle:
                configuration.PurchaseAllMissingHairstyles = enabled;
                break;
            case ShoppingCategory.Facewear:
                configuration.PurchaseAllMissingFacewear = enabled;
                break;
            case ShoppingCategory.Fashion:
                configuration.PurchaseAllMissingFashion = enabled;
                break;
        }
    }

    internal static void SetCategoryTabVisible(Configuration configuration, ShoppingCategory category, bool visible)
    {
        switch (category)
        {
            case ShoppingCategory.Outfit:
                configuration.ShowOutfitsTab = visible;
                break;
            case ShoppingCategory.Weapon:
                configuration.ShowWeaponsTab = visible;
                break;
            case ShoppingCategory.Minion:
                configuration.ShowMinionsTab = visible;
                break;
            case ShoppingCategory.Mount:
                configuration.ShowMountsTab = visible;
                break;
            case ShoppingCategory.Orchestrion:
                configuration.ShowOrchestrionsTab = visible;
                break;
            case ShoppingCategory.Hairstyle:
                configuration.ShowHairstylesTab = visible;
                break;
            case ShoppingCategory.Facewear:
                configuration.ShowFacewearTab = visible;
                break;
            case ShoppingCategory.Fashion:
                configuration.ShowFashionTab = visible;
                break;
        }
    }

}
