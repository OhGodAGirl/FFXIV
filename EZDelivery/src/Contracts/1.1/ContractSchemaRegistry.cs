using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Exportaramus.ConsumerKit.V1_1;

/// <summary>Offline Draft 2020-12 evaluator for every assertion vocabulary used by the pinned schemas.</summary>
public sealed class ContractSchemaRegistry
{
    private readonly Dictionary<string, JsonElement> schemas = new(StringComparer.Ordinal);
    public string CompactSchemaHash { get; }
    public static ContractSchemaRegistry Default { get; } = new();

    public ContractSchemaRegistry() : this(ReadEmbeddedSchemas()) { }

    public ContractSchemaRegistry(IEnumerable<byte[]> resources)
    {
        foreach (var bytes in resources)
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement.Clone();
            var id = root.GetProperty("$id").GetString()!;
            schemas.Add(id, root);
            if (id.EndsWith("/collection-data.schema.json", StringComparison.Ordinal))
                CompactSchemaHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        }
        if (CompactSchemaHash is null) throw new InvalidOperationException("Pinned compact schema missing.");
    }

    private static IEnumerable<byte[]> ReadEmbeddedSchemas()
    {
        var assembly = typeof(ContractSchemaRegistry).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".schema.json", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var memory = new MemoryStream(); stream.CopyTo(memory);
            yield return memory.ToArray();
        }
    }

    public bool Validate(JsonElement value, string fileName)
    {
        var pair = schemas.Single(p => p.Key.EndsWith("/" + fileName, StringComparison.Ordinal));
        return Evaluate(value, pair.Value, pair.Key, 0);
    }

    private bool Evaluate(JsonElement value, JsonElement schema, string documentId, int depth)
    {
        if (depth > 128) return false;
        if (schema.ValueKind is JsonValueKind.True) return true;
        if (schema.ValueKind is JsonValueKind.False) return false;
        bool Check(JsonElement child) => Evaluate(value, child, documentId, depth + 1);
        if (schema.TryGetProperty("$ref", out var reference))
        {
            var uri = new Uri(new Uri(documentId), reference.GetString());
            var baseId = uri.GetLeftPart(UriPartial.Path);
            if (!schemas.TryGetValue(baseId, out var target)) return false;
            foreach (var segment in uri.Fragment.TrimStart('#').Split('/', StringSplitOptions.RemoveEmptyEntries))
                target = target.GetProperty(Uri.UnescapeDataString(segment).Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal));
            if (!Evaluate(value, target, baseId, depth + 1)) return false;
        }
        if (schema.TryGetProperty("type", out var type))
        {
            bool Matches(string? t)
            {

                return t switch
                {
                    "object" => value.ValueKind == JsonValueKind.Object,
                    "array" => value.ValueKind == JsonValueKind.Array,
                    "string" => value.ValueKind == JsonValueKind.String,
                    "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                    "null" => value.ValueKind == JsonValueKind.Null,
                    "number" => value.ValueKind == JsonValueKind.Number,
                    "integer" => value.ValueKind == JsonValueKind.Number && ExactJsonNumber.Parse(value.GetRawText()).IsInteger,
                    _ => false,
                };
            }
            if (!(type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Any(t => Matches(t.GetString())) : Matches(type.GetString()))) return false;
        }
        if (schema.TryGetProperty("const", out var constant) && !Equivalent(value, constant)) return false;
        if (schema.TryGetProperty("enum", out var choices) && !choices.EnumerateArray().Any(c => Equivalent(value, c))) return false;
        if (schema.TryGetProperty("allOf", out var all) && !all.EnumerateArray().All(Check)) return false;
        if (schema.TryGetProperty("anyOf", out var any) && !any.EnumerateArray().Any(Check)) return false;
        if (schema.TryGetProperty("oneOf", out var one) && one.EnumerateArray().Count(Check) != 1) return false;
        if (schema.TryGetProperty("not", out var not) && Check(not)) return false;
        if (schema.TryGetProperty("if", out var condition))
        {
            var branch = Check(condition) ? "then" : "else";
            if (schema.TryGetProperty(branch, out var conditional) && !Check(conditional)) return false;
        }
        if (value.ValueKind == JsonValueKind.Number)
        {
            var n = ExactJsonNumber.Parse(value.GetRawText());
            if (schema.TryGetProperty("minimum", out var min) && n.CompareTo(ExactJsonNumber.Parse(min.GetRawText())) < 0) return false;
            if (schema.TryGetProperty("maximum", out var max) && n.CompareTo(ExactJsonNumber.Parse(max.GetRawText())) > 0) return false;
            if (schema.TryGetProperty("exclusiveMinimum", out var exmin) && n.CompareTo(ExactJsonNumber.Parse(exmin.GetRawText())) <= 0) return false;
            if (schema.TryGetProperty("exclusiveMaximum", out var exmax) && n.CompareTo(ExactJsonNumber.Parse(exmax.GetRawText())) >= 0) return false;
        }
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!; var length = text.EnumerateRunes().Count();
            if (schema.TryGetProperty("minLength", out var min) && length < min.GetInt32()) return false;
            if (schema.TryGetProperty("maxLength", out var max) && length > max.GetInt32()) return false;
            if (schema.TryGetProperty("pattern", out var pattern) && !Regex.IsMatch(text, pattern.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) return false;
            if (schema.TryGetProperty("format", out var format))
            {
                if (format.GetString() == "uuid" && !Guid.TryParseExact(text, "D", out _)) return false;
                if (format.GetString() == "date-time" && (!Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))) return false;
            }
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            var values = value.EnumerateArray().ToArray();
            if (schema.TryGetProperty("minItems", out var min) && values.Length < min.GetInt32()) return false;
            if (schema.TryGetProperty("maxItems", out var max) && values.Length > max.GetInt32()) return false;
            if (schema.TryGetProperty("uniqueItems", out var unique) && unique.GetBoolean())
            {
                var hashes = new Dictionary<string, List<byte[]>>(StringComparer.Ordinal);
                foreach (var item in values)
                {
                    var canonical = CanonicalValue(item);
                    var key = Convert.ToHexString(SHA256.HashData(canonical));
                    if (!hashes.TryGetValue(key, out var bucket)) hashes[key] = bucket = [];
                    if (bucket.Any(previous => previous.AsSpan().SequenceEqual(canonical))) return false;
                    bucket.Add(canonical);
                }
            }
            if (schema.TryGetProperty("items", out var items) && !values.All(v => Evaluate(v, items, documentId, depth + 1))) return false;
        }
        if (value.ValueKind == JsonValueKind.Object)
        {
            var values = value.EnumerateObject().ToArray();
            if (schema.TryGetProperty("minProperties", out var min) && values.Length < min.GetInt32()) return false;
            if (schema.TryGetProperty("maxProperties", out var max) && values.Length > max.GetInt32()) return false;
            if (schema.TryGetProperty("required", out var required) && !required.EnumerateArray().All(r => value.TryGetProperty(r.GetString()!, out _))) return false;
            schema.TryGetProperty("properties", out var properties);
            foreach (var property in values)
            {
                if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(property.Name, out var propertySchema))
                { if (!Evaluate(property.Value, propertySchema, documentId, depth + 1)) return false; }
                else if (schema.TryGetProperty("additionalProperties", out var additional) && !Evaluate(property.Value, additional, documentId, depth + 1)) return false;
            }
        }
        return true;
    }

    private static bool Equivalent(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind) return false;
        if (left.ValueKind == JsonValueKind.Number)
            return ExactJsonNumber.Parse(left.GetRawText()).CompareTo(ExactJsonNumber.Parse(right.GetRawText())) == 0;
        if (left.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
            return CanonicalValue(left).AsSpan().SequenceEqual(CanonicalValue(right));
        return JsonElement.DeepEquals(left, right);
    }

    private static byte[] CanonicalValue(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(value, writer);
        return stream.ToArray();

        static void Write(JsonElement element, Utf8JsonWriter writer)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)) { writer.WritePropertyName(property.Name); Write(property.Value, writer); }
                writer.WriteEndObject();
            }
            else if (element.ValueKind == JsonValueKind.Array) { writer.WriteStartArray(); foreach (var item in element.EnumerateArray()) Write(item, writer); writer.WriteEndArray(); }
            else if (element.ValueKind == JsonValueKind.Number) writer.WriteRawValue(ExactJsonNumber.Parse(element.GetRawText()).Canonical);
            else element.WriteTo(writer);
        }
    }
}
