using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CollectionExample;

/// <summary>Validates one Inventory V1 item response without caching it.</summary>
public static class InventoryItemReader
{
    // Inventory 1.0 does not define parser limits. Limit depth to 16 and locations to seven standard stores
    // plus ten retainers.
    public const int MaximumJsonBytes = 4 * 1024 * 1024;
    public sealed record Location(string Kind, string RetainerId, string Name, string State,
        DateTimeOffset ObservedAtUtc, uint? Normal, uint? HighQuality);
    public sealed record Result(string ContentId, long Revision, uint ItemId,
        ulong Normal, ulong HighQuality, bool UnknownLocations, bool RetainedLocations,
        IReadOnlyList<Location> Locations);

    /// <summary>Validates capabilities and the item reply for the requested item and character.</summary>
    /// <returns>The last observed result, or null if unavailable.</returns> <remarks> Invalid input throws;
    /// it does not mean zero items. Handle JSON, type, range, encoding and validation errors at the call
    /// site. Retained locations keep their original observation times and may be stale. </remarks>
    public static Result? Read(string capabilities, string payload, uint itemId, string expectedCharacter)
    {
        Require(itemId is > 0 and < 1_000_000 && Identity(expectedCharacter));
        using var caps = Parse(capabilities);
        var c = caps.RootElement;
        Fields(c, "schemaVersion scope quantityQuality retainedIsCurrent");
        Require(Text(c, "schemaVersion") == "1.0" && Text(c, "scope") == "CurrentCharacter");
        Require(!c.GetProperty("retainedIsCurrent").GetBoolean());
        var qualities = c.GetProperty("quantityQuality");
        Require(qualities.ValueKind == JsonValueKind.Array && qualities.GetArrayLength() == 2);
        Require(qualities[0].GetString() == "Normal" && qualities[1].GetString() == "HighQuality");

        using var document = Parse(payload);
        var root = document.RootElement;
        Fields(root, "schemaVersion available itemId contentId revision normalQuantity highQualityQuantity quantityMeaning hasUnknownLocations hasRetainedLocations locations");
        Require(Text(root, "schemaVersion") == "1.0" && root.GetProperty("itemId").GetUInt32() == itemId);
        Require(Text(root, "quantityMeaning") == "LastObservedQuantifiedLocationsOnly");
        var available = root.GetProperty("available").GetBoolean();
        var revision = root.GetProperty("revision").GetInt64();
        var normal = root.GetProperty("normalQuantity").GetUInt64();
        var high = root.GetProperty("highQualityQuantity").GetUInt64();
        var unknown = root.GetProperty("hasUnknownLocations").GetBoolean();
        var retained = root.GetProperty("hasRetainedLocations").GetBoolean();
        var rows = root.GetProperty("locations");
        Require(rows.ValueKind == JsonValueKind.Array && rows.GetArrayLength() <= 17);

        if (!available)
        {
            Require(root.GetProperty("contentId").ValueKind == JsonValueKind.Null &&
                revision == 0 && normal == 0 && high == 0 && unknown && !retained &&
                rows.GetArrayLength() == 0);
            return null;
        }

        var character = Text(root, "contentId");
        Require(character == expectedCharacter && Identity(character) && revision > 0);
        var locations = new List<Location>();
        var keys = new HashSet<(string, string)>();
        var retainerCount = 0;
        ulong sumNormal = 0, sumHigh = 0;
        foreach (var row in rows.EnumerateArray())
        {
            Fields(row, "kind retainerId name state observedAtUtc normalQuantity highQualityQuantity");
            var kind = Text(row, "kind");
            Require(kind is "Inventory" or "Equipped" or "ArmoryChest" or "ChocoboSaddlebag" or
                "PremiumSaddlebag" or "Retainer" or "Armoire" or "GlamourDresser");
            var retainerId = Text(row, "retainerId");
            Require(kind == "Retainer" ? Identity(retainerId) : retainerId == "0");
            Require(keys.Add((kind, retainerId)));
            if (kind == "Retainer") Require(++retainerCount <= 10);
            var name = Text(row, "name");
            Require(name.Length is > 0 and <= 64 && !name.Any(char.IsControl));
            var state = Text(row, "state");
            Require(state is "Observed" or "Retained" or "ReadFailed");
            Require(state == "Observed" || retained);
            var observed = row.GetProperty("observedAtUtc").GetDateTimeOffset();
            Require(observed != default && observed.Offset == TimeSpan.Zero);

            uint? nq = null, hq = null;
            if (kind is "Armoire" or "GlamourDresser")
            {
                Require(row.GetProperty("normalQuantity").ValueKind == JsonValueKind.Null &&
                    row.GetProperty("highQualityQuantity").ValueKind == JsonValueKind.Null);
            }
            else
            {
                nq = row.GetProperty("normalQuantity").GetUInt32();
                hq = row.GetProperty("highQualityQuantity").GetUInt32();
                Require((ulong)nq.Value + hq.Value is > 0 and <= uint.MaxValue);
                sumNormal = checked(sumNormal + nq.Value);
                sumHigh = checked(sumHigh + hq.Value);
            }
            locations.Add(new(kind, retainerId, name, state, observed, nq, hq));
        }
        Require(normal == sumNormal && high == sumHigh);
        return new(character, revision, itemId, normal, high, unknown, retained, locations.AsReadOnly());
    }

    private static JsonDocument Parse(string json)
    {
        Require(json.Length <= MaximumJsonBytes &&
            new UTF8Encoding(false, true).GetByteCount(json) <= MaximumJsonBytes);
        return JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
    }

    private static void Fields(JsonElement value, string names)
    {
        Require(value.ValueKind == JsonValueKind.Object);
        var remaining = names.Split(' ').ToHashSet(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            Require(remaining.Remove(property.Name));
        Require(remaining.Count == 0);
    }

    private static string Text(JsonElement value, string key) =>
        value.GetProperty(key).GetString() ?? throw new InvalidDataException("A required string is null.");

    private static bool Identity(string value) =>
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) &&
        id != 0 && value == id.ToString(CultureInfo.InvariantCulture);

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidDataException("The Inventory V1 response is not valid for this request.");
    }
}
