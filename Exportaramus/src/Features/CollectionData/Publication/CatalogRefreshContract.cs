using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Exportaramus.Bootstrap;
using Exportaramus.Features.CollectionData.Catalog;
namespace Exportaramus.Features.CollectionData.Publication;

// Additive September catalog receipt; the original contract and consumer kits remain frozen.
public sealed record CatalogRefreshContract(ImmutableDictionary<string, ImmutableHashSet<uint>> Additions, ImmutableArray<CatalogItem> Outfits)
{
    public const string GameDataVersion = "2026.09.01.0000.0000";
    public static CatalogRefreshContract LoadEmbedded() => Load(EmbeddedProductData.Read("catalog-refresh.json"));
    public static CatalogRefreshContract Load(byte[] bytes)
    {
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != "1e9d2084c311d888f7e1817ead5dd4b462292d7742bc628d7ea58d8172a87e7f")
            throw new InvalidDataException("Catalog refresh receipt mismatch.");
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var additions = root.GetProperty("additions").EnumerateObject().ToImmutableDictionary(p => p.Name,
            p => p.Value.EnumerateArray().Select(x => x.GetUInt32()).ToImmutableHashSet(), StringComparer.Ordinal);
        var outfits = root.GetProperty("outfits").EnumerateArray().Select(row =>
        {
            var id = row.GetProperty("id").GetUInt32();
            return new CatalogItem(id, id, "Outfit", true, id, [.. row.GetProperty("requiredItemIds").EnumerateArray().Select(x => x.GetUInt32())]);
        }).ToImmutableArray();
        return new(additions, outfits);
    }
    public FFXIVCollectContract Apply(FFXIVCollectContract baseline) => baseline with
    {
        GameDataVersion = GameDataVersion,
        RecognizedIds = baseline.RecognizedIds.SetItems(Additions.Select(pair =>
            KeyValuePair.Create(pair.Key, baseline.RecognizedIds[pair.Key].Union(pair.Value)))),
    };
    public DerivedCatalogInputs Apply(DerivedCatalogInputs baseline) => baseline with
    { Outfits = [.. baseline.Outfits, .. Outfits] };
}
