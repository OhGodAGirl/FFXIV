using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CollectionExporter;

internal static class CollectionPayload
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    internal static void Add(
        IDictionary<string, uint[]> target,
        string key,
        IEnumerable<uint> ids) =>
        target[key] = ids.Distinct().OrderBy(id => id).ToArray();

    internal static HashSet<uint> MergeOwned(
        IEnumerable<uint> profileIds,
        IEnumerable<uint>? earlierPassIds)
    {
        var merged = new HashSet<uint>(profileIds);
        if (earlierPassIds is not null)
            merged.UnionWith(earlierPassIds);

        return merged;
    }

    internal static string Serialize(IReadOnlyDictionary<string, uint[]> collections) =>
        JsonSerializer.Serialize(collections, SerializerOptions);
}
