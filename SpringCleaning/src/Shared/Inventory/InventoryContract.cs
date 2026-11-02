using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SpringCleaning.Shared.Inventory;

// Inventory V1 is intentionally validated independently of CollectionData.
internal static class InventoryContract
{
    public const int ByteLimit = 4 * 1024 * 1024;
    private static readonly string[] Kinds = ["Inventory", "Equipped", "ArmoryChest", "ChocoboSaddlebag", "PremiumSaddlebag", "Retainer",
        "Armoire", "GlamourDresser"];
    private static readonly string[] States = ["Unknown", "Observed", "Retained", "ReadFailed"];
    private static readonly Encoding StrictUtf8 = Encoding.GetEncoding(Encoding.UTF8.CodePage,
        EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    public static JsonDocument Parse(string payload, int limit = ByteLimit)
    {
        var bytes = StrictUtf8.GetBytes(payload);
        if (bytes.Length > limit)
            throw new JsonException("Payload exceeds the consumer bound.");
        var reader = new Utf8JsonReader(bytes);
        var objects = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
                objects.Push(new(StringComparer.Ordinal));
            if (reader.TokenType == JsonTokenType.PropertyName && !objects.Peek().Add(reader.GetString()!))
                throw new JsonException("Duplicate property.");
            if (reader.TokenType == JsonTokenType.EndObject)
                objects.Pop();
        }

        return JsonDocument.Parse(bytes);
    }

    public static void ValidateCapabilities(string payload)
    {
        using var document = Parse(payload, 16 * 1024);
        var root = document.RootElement;
        if (String(root, "schemaVersion") != "1.0" || String(root, "scope") != "CurrentCharacter" || root.GetProperty("retainedIsCurrent").GetBoolean())
            throw new JsonException("Unsupported Inventory capability.");
        var quality = root.GetProperty("quantityQuality").EnumerateArray().Select(item => item.GetString()).ToArray();
        if (!quality.SequenceEqual(["Normal", "HighQuality"]))
            throw new JsonException("Unsupported quantities.");
    }

    public static InventorySnapshot ReadSnapshot(string payload, string expectedCharacter)
    {
        using var document = Parse(payload);
        var root = document.RootElement;
        if (String(root, "schemaVersion") != "1.0")
            throw new JsonException("Unsupported Inventory schema.");
        if (!root.GetProperty("available").GetBoolean())
        {
            if (root.GetProperty("contentId").ValueKind != JsonValueKind.Null || UInt(root, "revision") != 0
                || root.GetProperty("locations").ValueKind != JsonValueKind.Null
                || root.GetProperty("retainerRosterKnown").GetBoolean())
                throw new JsonException("Malformed unavailable snapshot.");
            throw new InvalidOperationException("Inventory is unavailable.");
        }

        var character = String(root, "contentId");
        if (!Identifier(character) || character != expectedCharacter)
            throw new JsonException("Wrong character.");
        var revision = UInt(root, "revision");
        if (revision == 0)
            throw new JsonException("Invalid revision.");
        var locations = new List<InventoryLocation>();
        var keys = new HashSet<(string, string)>();
        var retainers = 0;
        foreach (var location in root.GetProperty("locations").EnumerateArray())
        {
            if (locations.Count >= 17)
                throw new JsonException("Too many locations.");
            var kind = String(location, "kind");
            var retainer = String(location, "retainerId");
            var state = String(location, "state");
            if (!Kinds.Contains(kind) || !States.Contains(state) || !keys.Add((kind, retainer))
                || (kind == "Retainer" ? !Identifier(retainer) || ++retainers > 10 : retainer != "0"))
                throw new JsonException("Invalid location.");
            if (String(location, "name").Length > 256)
                throw new JsonException("Name exceeds bounds.");
            var observed = Timestamp(location.GetProperty("observedAtUtc"));
            Timestamp(location.GetProperty("attemptedAtUtc"));
            var quantity = location.GetProperty("supportsQuantity").GetBoolean();
            if (quantity == (kind is "Armoire" or "GlamourDresser") || ((state is "Observed" or "Retained") && observed is null)
                || (state == "Unknown" && observed is not null))
                throw new JsonException("Inconsistent observation.");
            var items = new List<InventoryQuantity>();
            var ids = new HashSet<uint>();
            foreach (var item in location.GetProperty("items").EnumerateArray())
            {
                var id = UInt32(item, "itemId");
                if (id is 0 or >= 1_000_000 || !ids.Add(id) || items.Count >= 50_000)
                    throw new JsonException("Invalid item id.");
                var normal = OptionalQuantity(item.GetProperty("normalQuantity"));
                var high = OptionalQuantity(item.GetProperty("highQualityQuantity"));
                if (quantity ? normal is null || high is null || (normal == 0 && high == 0) : normal is not null || high is not null)
                    throw new JsonException("Invalid quantity meaning.");
                items.Add(new(id, normal, high));
            }

            if (observed is null && items.Count != 0)
                throw new JsonException("Unobserved items.");
            locations.Add(new(kind, retainer, state, observed, quantity, items.ToArray()));
        }

        return new(character, revision, root.GetProperty("retainerRosterKnown").GetBoolean(), locations.ToArray(),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))));
    }

    public static string String(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw new JsonException("Required string.");

    public static ulong UInt(JsonElement element, string name) => ExactUInt(element.GetProperty(name));

    public static uint UInt32(JsonElement element, string name) => checked((uint)UInt(element, name));

    private static ulong ExactUInt(JsonElement value)
    {
        var raw = value.GetRawText();
        var valid = ulong.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var number);
        if (!valid)
            throw new JsonException("Exact integer required.");
        return number;
    }

    private static uint? OptionalQuantity(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : checked((uint)ExactUInt(value));

    private static string? Timestamp(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        var text = value.GetString()!;
        string[] formats = ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd'T'HH:mm:sszzz",
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"];
        var valid = DateTimeOffset.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp);
        if (!valid)
            throw new JsonException("Invalid timestamp.");
        return text;
    }

    private static bool Identifier(string text) => text.Length is > 0 and <= 20 && (text.Length == 1 || text[0] != '0')
        && ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        && value != 0;
}
