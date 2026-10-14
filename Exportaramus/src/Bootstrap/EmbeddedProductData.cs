using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.Diagnostics;
using Exportaramus.Features.CollectionData.Publication;
namespace Exportaramus.Bootstrap;

public static class EmbeddedProductData
{
    public static byte[] Read(string suffix)
    {
        var assembly = typeof(EmbeddedProductData).Assembly;
        var name = assembly.GetManifestResourceNames().Single(name => name.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!; using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray();
    }
    public static DerivedCatalogInputs Derived()
    {
        return CatalogRefreshContract.LoadEmbedded().Apply(ParseDerived(Read("ffxiv-collect-contract.json"), Read("identifier-mappings.json"), Read("recognized-ids.json"), Read("derived-inputs.json")));
    }
    public static bool TryDerived(out DerivedCatalogInputs? value, out string? error)
        => TryRead(Derived, "Derived collection mappings are invalid.", out value, out error);
    public static bool TryDerived(byte[] contract, byte[] mappings, byte[] recognized, byte[] derived, out DerivedCatalogInputs? value, out string? error)
        => TryRead(() => ParseDerived(contract, mappings, recognized, derived), "Derived collection mappings are invalid.", out value, out error);
    public static DerivedCatalogInputs ParseDerived(byte[] contract, byte[] mappings, byte[] recognized, byte[] derived)
    {
        _ = FFXIVCollectContract.Load(contract, mappings, recognized, derived);
        using var document = JsonDocument.Parse(derived); var root = document.RootElement;
        var outfits = root.GetProperty("outfits").EnumerateArray().Select(value =>
        {
            var id = value.GetProperty("id").GetUInt32();
            return new CatalogItem(id, id, "Outfit", true, id, [.. value.GetProperty("requiredItemIds").EnumerateArray().Select(item => item.GetUInt32())]);
        }).ToImmutableArray();
        var relics = root.GetProperty("relics").EnumerateArray().Select(value =>
        {
            var id = value.GetProperty("id").GetUInt32(); var achievement = value.GetProperty("achievementId");
            return new CatalogItem(id, id, "Relic", true, id, [], achievement.ValueKind == JsonValueKind.Null ? null : achievement.GetUInt32());
        }).ToImmutableArray();
        return new(outfits, relics);
    }
    public static DiagnosticCollect DiagnosticContract()
    {
        return ParseDiagnosticContract(Read("ffxiv-collect-contract.json"), Read("identifier-mappings.json"));
    }
    public static bool TryDiagnosticContract(out DiagnosticCollect? value, out string? error)
        => TryRead(DiagnosticContract, "FFXIV Collect diagnostic metadata is invalid.", out value, out error);
    public static bool TryDiagnosticContract(byte[] contract, byte[] mappings, out DiagnosticCollect? value, out string? error)
        => TryRead(() => ParseDiagnosticContract(contract, mappings), "FFXIV Collect diagnostic metadata is invalid.", out value, out error);
    public static DiagnosticCollect ParseDiagnosticContract(byte[] bytes, byte[] mappings)
    {
        using var document = JsonDocument.Parse(bytes); var root = document.RootElement;
        var verified = root.GetProperty("verifiedAtUtc").GetDateTime();
        var commit = root.GetProperty("upstreamCommit").GetString();
        var mappingHash = Convert.ToHexStringLower(SHA256.HashData(mappings));
        if (verified.Kind != DateTimeKind.Utc || commit != "7eda07857e89b7dba81453135b44bc33bf3457b1" ||
            root.GetProperty("schemaVersion").GetString() != "1.0" || root.GetProperty("artifacts").GetProperty("identifierMappingsSha256").GetString() != mappingHash)
            throw new InvalidDataException("Diagnostic metadata does not match the approved contract.");
        return new()
        {
            ContractSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            MappingSha256 = mappingHash,
            VerifiedAtUtc = verified,
            UpstreamCommit = commit
        };
    }
    private static bool TryRead<T>(Func<T> read, string failure, out T? value, out string? error) where T : class
    {
        try { value = read(); error = null; return true; }
        catch (Exception e) when (e is InvalidDataException or JsonException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException)
        { value = null; error = failure; return false; }
    }
}

