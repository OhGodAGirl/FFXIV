using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Exportaramus.Features.CollectionData.Publication;

public sealed record BeastsExportContract(string GameDataVersion, ImmutableHashSet<uint> RecognizedIds, bool MappingValidated)
{
    public bool SupportsGameVersion(string version) => MappingValidated && (version == GameDataVersion ||
        GameDataVersion == CatalogRefreshContract.GameDataVersion && version == "2026.10.06.0000.0000");

    public static BeastsExportContract Load(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var ids = root.GetProperty("ids").EnumerateArray().Select(value => value.GetUInt32()).ToArray();
        var version = root.GetProperty("validatedGameDataVersion").GetString();
        if (root.GetProperty("schemaVersion").GetString() != "1.2" || root.GetProperty("key").GetString() != "beasts" ||
            root.GetProperty("upstreamCommit").GetString() != "30d92ee4ea0bbbd678fff5c4279a41f7724b357d" ||
            root.GetProperty("importerPath").GetString() != "app/controllers/import_controller.rb" ||
            string.IsNullOrWhiteSpace(version) || version.Length > 128 || ids.Length == 0 || ids.Any(id => id == 0) ||
            !ids.SequenceEqual(ids.Distinct().Order()) ||
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(',', ids)))) != root.GetProperty("idsSha256").GetString())
            throw new InvalidDataException("Beasts export contract is invalid.");
        return new(version, ids.ToImmutableHashSet(), root.GetProperty("mappingValidated").GetBoolean());
    }

    public static BeastsExportContract LoadEmbedded()
    {
        var assembly = typeof(BeastsExportContract).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith(".beasts-export-contract.json", StringComparison.Ordinal)))!;
        using var buffer = new MemoryStream(); stream.CopyTo(buffer);
        return Load(buffer.ToArray());
    }
}
