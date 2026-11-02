namespace CollectionShopper;

/// <summary>Catalog eligibility evaluated from plain facts read from native sheets.</summary>
internal static class CatalogEligibilityPolicy
{
    internal static bool IsMarketEligible(bool exists, bool untradable, uint searchCategory)
        => exists && !untradable && searchCategory != 0;

    internal static bool IsWeaponEligible(uint itemId, int majorCategory, uint uiCategory,
        bool glamorous, bool armoire, bool untradable, uint searchCategory)
        => itemId != 0 && (majorCategory == 1 || uiCategory == 11)
            && (glamorous || armoire) && !untradable && searchCategory != 0;

    internal static string? GameAssetPath(string? assetUrl)
    {
        if (string.IsNullOrWhiteSpace(assetUrl))
            return null;
        if (assetUrl.StartsWith("ui/", StringComparison.OrdinalIgnoreCase))
            return assetUrl;
        if (!Uri.TryCreate(assetUrl, UriKind.Absolute, out var uri))
            return null;

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0].Equals("path", StringComparison.OrdinalIgnoreCase))
            {
                var path = Uri.UnescapeDataString(parts[1]);
                return path.StartsWith("ui/", StringComparison.OrdinalIgnoreCase) ? path : null;
            }
        }

        return null;
    }
}
