namespace Exportaramus.Features.CollectionData.Catalog;

public sealed class CollectionCatalogCache
{
    private CollectionCatalog? current;

    public CollectionCatalog? Get(string language, string version)
    {
        var value = current;
        if (value is null) return null;
        return value.ClientLanguage == language && value.GameDataVersion == version ? value : null;
    }

    public void Set(CollectionCatalog catalog) => current = catalog;

    public void Invalidate() => current = null;
}
