using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Exportaramus.Features.CollectionData.Contracts;
namespace Exportaramus.Features.CollectionData.Publication;

public sealed record FFXIVCollectContract(string GameDataVersion, ImmutableDictionary<string, ImmutableHashSet<uint>> RecognizedIds)
{
    public BeastsExportContract? Beasts { get; init; }
    // Frozen August and September mappings were rechecked against October game data.
    public bool SupportsGameVersion(string version) => version == GameDataVersion ||
        GameDataVersion == "2026.08.11.0000.0000" && version == "2026.09.01.0000.0000" ||
        GameDataVersion is "2026.08.11.0000.0000" or CatalogRefreshContract.GameDataVersion && version == "2026.10.06.0000.0000";
    public static FFXIVCollectContract LoadEmbedded()
    {
        byte[] Resource(string suffix)
        {
            var assembly = typeof(FFXIVCollectContract).Assembly;
            var name = assembly.GetManifestResourceNames().Single(name => name.EndsWith(suffix, StringComparison.Ordinal));
            using var stream = assembly.GetManifestResourceStream(name)!; using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
        }
        return CatalogRefreshContract.LoadEmbedded().Apply(Load(Resource("ffxiv-collect-contract.json"), Resource("identifier-mappings.json"), Resource("recognized-ids.json"), Resource("derived-inputs.json")))
            with { Beasts = BeastsExportContract.LoadEmbedded() };
    }

    public static bool TryLoadEmbedded(out FFXIVCollectContract? contract, out string? error)
        => TryLoad(LoadEmbedded, out contract, out error);

    public static bool TryLoad(byte[] contractBytes, byte[] mapping, byte[] recognized, byte[] derived, out FFXIVCollectContract? contract, out string? error)
        => TryLoad(() => Load(contractBytes, mapping, recognized, derived), out contract, out error);

    private static bool TryLoad(Func<FFXIVCollectContract> load, out FFXIVCollectContract? contract, out string? error)
    {
        try { contract = load(); error = null; return true; }
        catch (Exception e) when (e is InvalidDataException or JsonException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException)
        { contract = null; error = "FFXIV Collect mapping contract is invalid."; return false; }
    }

    public static FFXIVCollectContract Load(byte[] contractBytes, byte[] mapping, byte[] recognized, byte[] derived)
    {
        using var contract = JsonDocument.Parse(contractBytes); var root = contract.RootElement;
        var expected = root.GetProperty("artifacts");
        foreach (var pair in new[] { ("identifierMappingsSha256", mapping), ("recognizedIdsSha256", recognized), ("derivedInputsSha256", derived) })
            if (Convert.ToHexStringLower(SHA256.HashData(pair.Item2)) != expected.GetProperty(pair.Item1).GetString()) throw new InvalidDataException("Embedded mapping hash mismatch.");
        if (root.GetProperty("schemaVersion").GetString() != "1.0" || root.GetProperty("importUtf8ByteLimit").GetInt32() != 1048576 ||
            !root.GetProperty("orderedKeys").EnumerateArray().Select(value => value.GetString()).SequenceEqual(CollectionKeys.Collect)) throw new InvalidDataException("Embedded import contract mismatch.");
        var gameVersion = root.GetProperty("validatedGameDataVersion").GetString();
        if (string.IsNullOrWhiteSpace(gameVersion) || gameVersion.Length > 128 || root.GetProperty("format").GetString() != "exportaramus-ffxiv-collect-contract") throw new InvalidDataException("Embedded metadata is invalid.");
        using var mappingDocument = JsonDocument.Parse(mapping);
        using var ids = JsonDocument.Parse(recognized);
        using var derivedDocument = JsonDocument.Parse(derived);
        foreach (var document in new[] { root, mappingDocument.RootElement, ids.RootElement, derivedDocument.RootElement })
            if (document.GetProperty("schemaVersion").GetString() != "1.0" || document.GetProperty("upstreamRepository").GetString() != "https://github.com/skyborn-industries/ffxiv-collect" || document.GetProperty("upstreamCommit").GetString() != "7eda07857e89b7dba81453135b44bc33bf3457b1") throw new InvalidDataException("Embedded source baseline mismatch.");
        if (mappingDocument.RootElement.GetProperty("validatedGameDataVersion").GetString() != gameVersion || derivedDocument.RootElement.GetProperty("validatedGameDataVersion").GetString() != gameVersion) throw new InvalidDataException("Embedded game-data compatibility mismatch.");
        if (!mappingDocument.RootElement.GetProperty("entries").EnumerateArray().Select(value => value.GetProperty("key").GetString()).SequenceEqual(CollectionKeys.Collect)) throw new InvalidDataException("Embedded identifier mapping coverage mismatch.");
        var catalogs = ids.RootElement.GetProperty("catalogs").EnumerateArray().ToArray();
        if (!catalogs.Select(value => value.GetProperty("key").GetString()).SequenceEqual(CollectionKeys.Collect)) throw new InvalidDataException("Embedded catalog coverage mismatch.");
        var map = ImmutableDictionary.CreateBuilder<string, ImmutableHashSet<uint>>(StringComparer.Ordinal);
        foreach (var catalog in catalogs)
        {
            var values = catalog.GetProperty("ids").EnumerateArray().Select(value => value.GetUInt32()).ToArray();
            var hash = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join(',', values))));
            if (values.Length != catalog.GetProperty("count").GetInt32() || values.Any(value => value == 0) ||
                !values.SequenceEqual(values.Distinct().Order()) || hash != catalog.GetProperty("idsSha256").GetString()) throw new InvalidDataException("Embedded catalog IDs are invalid.");
            map.Add(catalog.GetProperty("key").GetString()!, values.ToImmutableHashSet());
        }
        foreach (var catalog in root.GetProperty("catalogs").EnumerateArray())
        {
            var match = catalogs.Single(c => c.GetProperty("key").GetString() == catalog.GetProperty("key").GetString());
            if (!JsonElement.DeepEquals(catalog.GetProperty("count"), match.GetProperty("count")) || !JsonElement.DeepEquals(catalog.GetProperty("idsSha256"), match.GetProperty("idsSha256"))) throw new InvalidDataException("Embedded manifest catalog mismatch.");
        }
        foreach (var key in new[] { "outfits", "relics" })
        {
            var rows = derivedDocument.RootElement.GetProperty(key).EnumerateArray().ToArray();
            if (!rows.Select(row => row.GetProperty("id").GetUInt32()).SequenceEqual(map[key].Order())) throw new InvalidDataException("Embedded derived catalog mismatch.");
            var normalized = key == "outfits"
                ? string.Join('\n', rows.Select(row => row.GetProperty("id").GetUInt32() + ":" + string.Join(',', row.GetProperty("requiredItemIds").EnumerateArray().Select(id => id.GetUInt32()))))
                : string.Join('\n', rows.Select(row => row.GetProperty("id").GetUInt32() + ":" + (row.GetProperty("achievementId").ValueKind == JsonValueKind.Null ? 0u : row.GetProperty("achievementId").GetUInt32())));
            var hash = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)));
            var hashKey = key + "Sha256";
            if (hash != root.GetProperty("relationshipHashes").GetProperty(hashKey).GetString() || hash != derivedDocument.RootElement.GetProperty("relationshipHashes").GetProperty(hashKey).GetString()) throw new InvalidDataException("Embedded derived relationship hash mismatch.");
        }
        if (!Exportaramus.ConsumerKit.V1.ContractSchemaRegistry.Default.Validate(derivedDocument.RootElement, "derived-inputs.schema.json")) throw new InvalidDataException("Embedded derived input schema mismatch.");
        return new(gameVersion, map.ToImmutable());
    }
}

