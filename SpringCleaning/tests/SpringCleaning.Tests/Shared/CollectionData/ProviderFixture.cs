using System.Text.Json.Nodes;
using SpringCleaning.Shared.Inventory;

namespace SpringCleaning.Tests.CollectionData;

internal static class ProviderFixture
{
    internal static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
    internal const string InventoryCapabilities = """{"schemaVersion":"1.0","scope":"CurrentCharacter","quantityQuality":["Normal","HighQuality"],"retainedIsCurrent":false}""";
    internal static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "tests", "fixtures", "Contracts", "1.2")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new DirectoryNotFoundException();
        }
    }
    internal static JsonObject Snapshot(string name = "compact-valid-full.json")
    {
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "tests", "fixtures", "Contracts", "1.2", "fixtures", "valid", name)))!.AsObject();
        root["contentId"] = "42";
        return root;
    }
    internal static string Capabilities() => File.ReadAllText(Path.Combine(Root, "tests", "fixtures", "Contracts", "1.2", "fixtures", "valid", "capabilities-valid.json"));
    internal static JsonObject Presence(JsonNode root, string key) => root["currentItemPresence"]![key]!.AsObject();
    internal static JsonObject Health(JsonNode root, string key) => root["sourceHealth"]![key]!.AsObject();
    internal static void Items(JsonNode root, string key, IEnumerable<uint> ids)
    {
        Presence(root, key)["items"] = new JsonArray(ids.Select(id =>
        {
            var item = new JsonObject { ["itemId"] = id };
            if (key is not ("glamourDresser" or "armoire")) item["quantity"] = 1u;
            return (JsonNode?)item;
        }).ToArray());
        if (key == "glamourDresser")
            Presence(root, key).Remove("dresserEntries");
    }
    internal static string Inventory(JsonNode? collection = null, ulong revision = 1, string state = "Observed")
    {
        collection ??= Snapshot();
        var locations = new JsonArray();
        foreach (var (key, kind) in new[] { ("inventory", "Inventory"), ("armoryChest", "ArmoryChest") })
        {
            var items = Presence(collection, key)["items"]!.AsArray().Select(item => (JsonNode?)new JsonObject
            {
                ["itemId"] = item!["itemId"]!.GetValue<uint>(),
                ["normalQuantity"] = item["quantity"]?.GetValue<uint>() ?? 1,
                ["highQualityQuantity"] = 0,
            }).ToArray();
            locations.Add(new JsonObject
            {
                ["kind"] = kind,
                ["retainerId"] = "0",
                ["name"] = kind,
                ["state"] = state,
                ["observedAtUtc"] = "2026-09-06T12:00:00Z",
                ["attemptedAtUtc"] = "2026-09-06T12:00:00Z",
                ["supportsQuantity"] = true,
                ["items"] = new JsonArray(items),
            });
        }
        var equipped = locations[1]!.DeepClone();
        equipped["kind"] = "Equipped";
        equipped["name"] = "Equipped";
        equipped["items"] = new JsonArray();
        locations.Add(equipped);
        return new JsonObject
        {
            ["schemaVersion"] = "1.0",
            ["available"] = true,
            ["contentId"] = collection["contentId"]!.GetValue<string>(),
            ["revision"] = revision,
            ["retainerRosterKnown"] = false,
            ["locations"] = locations,
        }.ToJsonString();
    }
    internal static InventorySnapshot InventoryObservation(JsonNode? collection = null, ulong revision = 1) =>
        InventoryContract.ReadSnapshot(Inventory(collection, revision), collection?["contentId"]?.GetValue<string>() ?? "42");
}
