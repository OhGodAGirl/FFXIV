using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1;
namespace Exportaramus.Features.CollectionData.Publication;

// Keep the pinned 1.0 contract exact while both gates publish the same accepted capture.
public static class CollectionDataLegacyProjection
{
    public static ImmutableArray<string> CompactKeys { get; } = [.. CollectionDataSemanticValidator.UnlockKeys, .. CollectionDataSemanticValidator.PresenceKeys, "reportedOwnership"];
    public static ImmutableArray<string> CompactKeysV11 { get; } = [.. Exportaramus.ConsumerKit.V1_1.CollectionDataSemanticValidator.UnlockKeys, .. CollectionDataSemanticValidator.PresenceKeys, "reportedOwnership"];
    public static CollectionData DataV11(CollectionData data) => data with
    {
        State = data.State with { Unlocks = data.State.Unlocks.Remove("beasts") },
        Collections = LegacyCollections(data).Remove("beasts"),
        SourceHealth = data.SourceHealth.Remove("beasts"),
    };
    public static CollectionData Data(CollectionData data) => data with
    {
        State = data.State with { Unlocks = data.State.Unlocks.Remove("facewear").Remove("beasts") },
        Collections = LegacyCollections(data).Remove("facewear").Remove("beasts"),
        SourceHealth = data.SourceHealth.Remove("facewear").Remove("beasts"),
    };
    public static CollectionData RichV12(CollectionData data) => data with { Collections = LegacyCollections(data) };
    private static ImmutableDictionary<string, CollectionResult> LegacyCollections(CollectionData data) =>
        data.Collections.TryGetValue("outfits", out var outfits)
            ? data.Collections.SetItem("outfits", outfits with { Items = [.. outfits.Items.Where(item => item.Id is not (52601 or 52602))] })
            : data.Collections;
    public static string Capabilities(string canonical) => CapabilitiesCore(canonical, false);
    public static string CapabilitiesV11(string canonical) => CapabilitiesCore(canonical, true);
    private static string CapabilitiesCore(string canonical, bool v11)
    {
        var root = JsonNode.Parse(canonical)!.AsObject();
        root["schemaVersion"] = v11 ? "1.1" : "1.0";
        root["sourceCapabilities"]!.AsObject().Remove("beasts");
        if (!v11) root["sourceCapabilities"]!.AsObject().Remove("facewear");
        var keys = v11 ? Exportaramus.ConsumerKit.V1_1.CollectionDataSemanticValidator.UnlockKeys : CollectionDataSemanticValidator.UnlockKeys;
        root["unlockKeys"] = new JsonArray(keys.Select(key => (JsonNode?)JsonValue.Create(key)).ToArray());
        foreach (var gate in root["gates"]!.AsObject().ToArray())
            root["gates"]![gate.Key] = gate.Value!.GetValue<string>().Replace(".V1_2.", ".V1_1.", StringComparison.Ordinal).Replace(".V1_1.", v11 ? ".V1_1." : ".", StringComparison.Ordinal);
        root["collectionDataSchema"]!["id"] = $"https://exportaramus.local/schemas/{(v11 ? "1.1" : "1.0")}/collection-data.schema.json";
        root["collectionDataSchema"]!["sha256"] = v11 ? Exportaramus.ConsumerKit.V1_1.CollectionDataSemanticValidator.CompactSchemaHash : CollectionDataSemanticValidator.CompactSchemaHash;
        return root.ToJsonString();
    }
    public static string? Notice(string canonical) => NoticeCore(canonical, false);
    public static string? NoticeV11(string canonical) => NoticeCore(canonical, true);
    private static string? NoticeCore(string canonical, bool v11)
    {
        var root = JsonNode.Parse(canonical)!.AsObject();
        root["schemaVersion"] = v11 ? "1.1" : "1.0";
        if (root["changedSources"] is JsonArray sources)
        {
            var retained = sources.Select(node => node!.GetValue<string>()).Where(key => key != "beasts" && (v11 || key != "facewear")).ToArray();
            if (retained.Length == 0) return null;
            root["changedSources"] = new JsonArray(retained.Select(key => (JsonNode?)JsonValue.Create(key)).ToArray());
        }
        return root.ToJsonString();
    }
}


