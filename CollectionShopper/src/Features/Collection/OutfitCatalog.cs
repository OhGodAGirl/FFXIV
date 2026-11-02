using System.Reflection;

namespace CollectionShopper;

internal static class OutfitCatalog
{
    private const string ResourceName = "CollectionShopper.Data.outfits.json";
    private static readonly Lazy<IReadOnlyList<Outfit>> BundledCatalog = new(LoadBundledCore);

    public static IReadOnlyList<Outfit> LoadBundled() => BundledCatalog.Value;

    internal static IReadOnlyList<Outfit> LoadResource(Stream? resource)
    {
        using var stream = resource
            ?? throw new InvalidDataException($"The bundled outfit catalog resource '{ResourceName}' is missing.");
        return OutfitCatalogParser.Parse(stream);
    }

    private static IReadOnlyList<Outfit> LoadBundledCore()
        => LoadResource(typeof(OutfitCatalog).Assembly.GetManifestResourceStream(ResourceName));

}
