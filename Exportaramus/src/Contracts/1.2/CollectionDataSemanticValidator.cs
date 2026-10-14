using System.Globalization;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Exportaramus.ConsumerKit.V1_2;

public static class CollectionDataSemanticValidator
{
    public static ImmutableArray<string> UnlockKeys { get; } = ["mounts", "minions", "emotes", "bardings", "cards", "npcs", "fashions", "frames", "occult_records", "orchestrions", "spells", "aetherytes", "chocobo_routes", "leves", "hairstyles", "field_records", "survey_records", "quests", "achievements", "facewear", "beasts"];
    public static ImmutableArray<string> PresenceKeys { get; } = ["inventory", "armoryChest", "chocoboSaddlebag", "premiumSaddlebag", "retainers", "armoire", "glamourDresser"];
    public static ImmutableArray<string> RichKeys { get; } = [.. UnlockKeys, "armoires", "glamour_dresser", "outfits", "relics"];
    public static ImmutableArray<string> ReportCodes { get; } = ["Accepted", "InvalidRequest", "WrongCharacter", "RateLimited", "CapacityReached", "QueueFull", "Unavailable", "ProviderStopping"];
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Lazy<JsonElement> DerivedInputs = new(() =>
    {
        var assembly = typeof(CollectionDataSemanticValidator).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith(".derived-inputs.json", StringComparison.Ordinal)))!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    });
    private static readonly Lazy<HashSet<uint>> RecognizedBeasts = new(() =>
    {
        var assembly = typeof(CollectionDataSemanticValidator).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith(".recognized-ids.json", StringComparison.Ordinal)))!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("catalogs").EnumerateArray().Single(c => c.GetProperty("key").GetString() == "beasts")
            .GetProperty("ids").EnumerateArray().Select(id => id.GetUInt32()).ToHashSet();
    });
    public static string CompactSchemaHash => ContractSchemaRegistry.Default.CompactSchemaHash;

    public static CollectionDataValidationResult<ValidatedJsonDocument> ValidateFFXIVCollect(string? payload)
    {
        try { return ValidateFFXIVCollectCore(payload); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or ArgumentException)
        { return CollectionDataValidationResult<ValidatedJsonDocument>.Invalid(CollectionDataValidationCode.ContractBindingFailed); }
    }

    private static CollectionDataValidationResult<ValidatedJsonDocument> ValidateFFXIVCollectCore(string? payload)
    {
        var parsed = ParseDocument(payload, 1048576, false);
        if (!parsed.Success) return CollectionDataValidationResult<ValidatedJsonDocument>.Invalid(parsed.Code);
        using var document = parsed.Value!;
        if (!ContractSchemaRegistry.Default.Validate(document.RootElement, "import.schema.json")) return CollectionDataValidationResult<ValidatedJsonDocument>.Invalid(CollectionDataValidationCode.ContractBindingFailed);
        var assembly = typeof(CollectionDataSemanticValidator).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith(".recognized-ids.json", StringComparison.Ordinal)))!;
        using var recognized = JsonDocument.Parse(stream);
        var catalogs = recognized.RootElement.GetProperty("catalogs").EnumerateArray().ToArray();
        var order = catalogs.Select(c => c.GetProperty("key").GetString()!).ToArray();
        var previous = -1;
        foreach (var entry in document.RootElement.EnumerateObject())
        {
            var index = Array.IndexOf(order, entry.Name);
            if (index <= previous || !Ascending(entry.Value, null)) return CollectionDataValidationResult<ValidatedJsonDocument>.Invalid(CollectionDataValidationCode.SemanticValidationFailed);
            var allowlist = catalogs[index].GetProperty("ids").EnumerateArray().Select(id => id.GetUInt32()).ToHashSet();
            if (entry.Value.EnumerateArray().Any(id => !allowlist.Contains(id.GetUInt32()))) return CollectionDataValidationResult<ValidatedJsonDocument>.Invalid(CollectionDataValidationCode.SemanticValidationFailed);
            previous = index;
        }
        return CollectionDataValidationResult<ValidatedJsonDocument>.Valid(new(document.RootElement.Clone()));
    }

    public static CollectionDataValidationResult<CollectionDataSnapshot> ValidateSnapshot(string? payload, string expectedContentId) =>
        Parse(payload, 4194304, "collection-data.schema.json", CollectionDataJsonContext.Default.CollectionDataSnapshot, root =>
        {
            var identity = Identity(root, expectedContentId);
            return identity != CollectionDataValidationCode.None ? identity : ValidateMaps(root, false) && ValidateReports(root.GetProperty("reportedOwnership").GetProperty("items")) ? CollectionDataValidationCode.None : CollectionDataValidationCode.SemanticValidationFailed;
        });

    public static CollectionDataValidationResult<CollectionDataCapabilities> ValidateCapabilities(string? payload) =>
        Parse(payload, 16384, "capabilities.schema.json", CollectionDataJsonContext.Default.CollectionDataCapabilities, root =>
        {
            var schema = root.GetProperty("collectionDataSchema");
            if (schema.GetProperty("sha256").GetString() != CompactSchemaHash) return CollectionDataValidationCode.SchemaHashMismatch;
            var caps = root.GetProperty("sourceCapabilities");
            if (!ExactKeys(caps, [.. UnlockKeys, .. PresenceKeys, "reportedOwnership"]) ||
                !ExactArray(root.GetProperty("unlockKeys"), UnlockKeys) || !ExactArray(root.GetProperty("currentPresenceSources"), PresenceKeys)) return CollectionDataValidationCode.SemanticValidationFailed;
            foreach (var entry in caps.EnumerateObject()) if (!Capability(entry.Name, entry.Value)) return CollectionDataValidationCode.SemanticValidationFailed;
            var limits = root.GetProperty("limits");
            bool Rate(string key, int rate, int burst) => limits.GetProperty(key).GetProperty("acceptedPerMinute").GetInt32() == rate && limits.GetProperty(key).GetProperty("burst").GetInt32() == burst;
            return Rate("globalReportRate", 60, 20) && Rate("reporterReportRate", 30, 10) ? CollectionDataValidationCode.None : CollectionDataValidationCode.SemanticValidationFailed;
        });

    public static CollectionDataValidationResult<CollectionDataChangedNotice> ValidateChanged(string? payload) =>
        Parse(payload, 4096, "snapshot-changed.schema.json", CollectionDataJsonContext.Default.CollectionDataChangedNotice, root =>
        {
            var identity = Identity(root, null); if (identity != CollectionDataValidationCode.None) return identity;
            var types = root.GetProperty("changedDataTypes").EnumerateArray().Select(x => x.GetString()!).ToArray();
            var sources = root.GetProperty("changedSources").EnumerateArray().Select(x => x.GetString()!).ToArray();
            bool Matches(string key) => types.Contains("SourceCapabilities") || types.Contains("SourceHealth") ||
                (UnlockKeys.Contains(key) && types.Contains("Unlocks")) || (PresenceKeys.Contains(key) && types.Contains("CurrentItemPresence")) || (key == "reportedOwnership" && types.Contains("ReportedOwnership"));
            return Sorted(types) && Sorted(sources) && sources.All(Matches) ? CollectionDataValidationCode.None : CollectionDataValidationCode.SemanticValidationFailed;
        });

    public static CollectionDataValidationResult<CollectionDataWithdrawnNotice> ValidateWithdrawn(string? payload) =>
        Parse(payload, 4096, "snapshot-withdrawn.schema.json", CollectionDataJsonContext.Default.CollectionDataWithdrawnNotice, root => Identity(root, null));

    public static CollectionDataValidationResult<RichSnapshotDocument> ValidateRich(string? payload)
    {
        var parsed = ParseDocument(payload, 16777216, true);
        if (!parsed.Success) return CollectionDataValidationResult<RichSnapshotDocument>.Invalid(parsed.Code);
        using var document = parsed.Value!;
        try
        {
            if (!ContractSchemaRegistry.Default.Validate(document.RootElement, "exportaramus-snapshot.schema.json")) return CollectionDataValidationResult<RichSnapshotDocument>.Invalid(CollectionDataValidationCode.ContractBindingFailed);
            if (!ValidateRich(document.RootElement)) return CollectionDataValidationResult<RichSnapshotDocument>.Invalid(CollectionDataValidationCode.SemanticValidationFailed);
            return CollectionDataValidationResult<RichSnapshotDocument>.Valid(new(document.RootElement.Clone()));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or ArgumentException)
        { return CollectionDataValidationResult<RichSnapshotDocument>.Invalid(CollectionDataValidationCode.ContractBindingFailed); }
    }

    public static bool ValidateRich(JsonElement root)
    {
        if (!ValidateMaps(root, true)) return false;
        var collections = root.GetProperty("collections"); var omitted = root.GetProperty("omittedCollections"); var health = root.GetProperty("sourceHealth");
        if (!collections.EnumerateObject().Select(x => x.Name).Concat(omitted.EnumerateObject().Select(x => x.Name)).Order(StringComparer.Ordinal).SequenceEqual(RichKeys.Order(StringComparer.Ordinal))) return false;
        foreach (var entry in collections.EnumerateObject())
        {
            var collection = entry.Value; var h = health.GetProperty(entry.Name);
            if (collection.GetProperty("ownershipKind").GetString() != DataType(entry.Name)) return false;
            foreach (var field in new[] { "sourceStatus", "completeness", "provenanceType", "observedAtUtc" })
                if (!h.TryGetProperty(field, out var expected) || !JsonElement.DeepEquals(collection.GetProperty(field), expected)) return false;
            if (!Usable(h)) return false;
            uint previous = 0;
            foreach (var item in collection.GetProperty("items").EnumerateArray())
            {
                var id = item.GetProperty("id").GetUInt32(); if (id <= previous) return false; previous = id;
                if (entry.Name == "beasts" && !RecognizedBeasts.Value.Contains(id)) return false;
                var evidence = item.GetProperty("ownershipEvidence"); var rule = evidence.GetProperty("ruleCode").GetString();
                var facts = evidence.GetProperty("facts").EnumerateArray().ToArray();
                var dataType = DataType(entry.Name);
                if (facts.Any(f => f.GetProperty("dataType").GetString() != DataType(f.GetProperty("sourceKey").GetString()!))) return false;
                if (facts.Any(f => f.GetProperty("state").GetString() == "Unknown" || f.GetProperty("sourceStatus").GetString() is not ("Fresh" or "Stale"))) return false;
                foreach (var fact in facts)
                {
                    var key = fact.GetProperty("sourceKey").GetString()!;
                    if (!health.TryGetProperty(key, out var fh) || !Usable(fh) || !JsonElement.DeepEquals(fh.GetProperty("sourceStatus"), fact.GetProperty("sourceStatus"))) return false;
                    if (fact.GetProperty("dataType").GetString() == "CurrentItemPresence")
                    {
                        var sources = root.GetProperty("currentItemPresence");
                        if (!sources.TryGetProperty(key, out var source)) return false;
                        var present = source.GetProperty("items").EnumerateArray().Any(i => i.GetProperty("itemId").GetUInt32() == fact.GetProperty("subjectId").GetUInt32());
                        if (present != (fact.GetProperty("state").GetString() == "Present")) return false;
                    }
                }
                bool Positive(JsonElement fact) => fact.GetProperty("state").GetString() == "Present";
                bool owned;
                if (rule == "DirectPermanentUnlock" || rule == "QuestCompletion")
                {
                    if (dataType != "PermanentUnlock" || facts.Length != 1 || facts[0].GetProperty("sourceKey").GetString() != entry.Name || facts[0].GetProperty("subjectId").GetUInt32() != id) return false;
                    owned = Positive(facts[0]);
                }
                else if (rule == "DirectCurrentItemPresence")
                {
                    if (dataType != "CurrentItemPresence" || facts.Any(f => f.GetProperty("dataType").GetString() != "CurrentItemPresence")) return false;
                    var expectedSource = entry.Name == "armoires" ? "armoire" : "glamourDresser";
                    if (facts.Length != 1 || facts[0].GetProperty("sourceKey").GetString() != expectedSource || (entry.Name == "glamour_dresser" && facts[0].GetProperty("subjectId").GetUInt32() != id)) return false;
                    owned = facts.Any(Positive);
                }
                else if (rule == "AllRequiredConstituents")
                {
                    if (entry.Name != "outfits" || facts.Any(f => f.GetProperty("dataType").GetString() != "CurrentItemPresence")) return false;
                    var outfit = DerivedInputs.Value.GetProperty("outfits").EnumerateArray().FirstOrDefault(x => x.GetProperty("id").GetUInt32() == id);
                    if (outfit.ValueKind == JsonValueKind.Undefined || !outfit.GetProperty("requiredItemIds").EnumerateArray().Select(x => x.GetUInt32()).SequenceEqual(facts.Select(f => f.GetProperty("subjectId").GetUInt32()).Distinct().Order())) return false;
                    foreach (var group in facts.GroupBy(f => f.GetProperty("subjectId").GetUInt32()))
                        if (!group.Any(Positive) && !group.Select(f => f.GetProperty("sourceKey").GetString()!).Order(StringComparer.Ordinal).SequenceEqual(PresenceKeys.Order(StringComparer.Ordinal))) return false;
                    owned = facts.GroupBy(f => f.GetProperty("subjectId").GetUInt32()).All(g => g.Any(Positive));
                }
                else if (rule == "PermanentUnlockOrCurrentItemPresence")
                {
                    if (entry.Name != "relics") return false;
                    var relic = DerivedInputs.Value.GetProperty("relics").EnumerateArray().FirstOrDefault(x => x.GetProperty("id").GetUInt32() == id);
                    if (relic.ValueKind == JsonValueKind.Undefined) return false;
                    var achievement = relic.GetProperty("achievementId");
                    foreach (var fact in facts)
                    {
                        if (fact.GetProperty("dataType").GetString() == "CurrentItemPresence") { if (fact.GetProperty("subjectId").GetUInt32() != id) return false; }
                        else if (fact.GetProperty("sourceKey").GetString() != "achievements" || achievement.ValueKind == JsonValueKind.Null || fact.GetProperty("subjectId").GetUInt32() != achievement.GetUInt32()) return false;
                    }
                    if (!facts.Any(Positive))
                    {
                        if (!facts.Where(f => f.GetProperty("dataType").GetString() == "CurrentItemPresence").Select(f => f.GetProperty("sourceKey").GetString()!).Order(StringComparer.Ordinal).SequenceEqual(PresenceKeys.Order(StringComparer.Ordinal))) return false;
                        if (achievement.ValueKind != JsonValueKind.Null && !facts.Any(f => f.GetProperty("sourceKey").GetString() == "achievements")) return false;
                    }
                    owned = facts.Any(Positive);
                }
                else return false;
                if (item.GetProperty("owned").GetBoolean() != owned) return false;
                if (entry.Name == "quests")
                {
                    if (!item.TryGetProperty("status", out var status) || owned != (status.GetString() == "Completed") || rule != "QuestCompletion") return false;
                    foreach (var forbidden in new[] { "description", "group", "category", "series", "level", "location", "expansion", "festival", "repeatable", "points", "rewards" }) if (item.TryGetProperty(forbidden, out _)) return false;
                }
                if (entry.Name == "achievements" && (!item.TryGetProperty("rewardCollected", out var reward) || reward.ValueKind != JsonValueKind.Null)) return false;
                if (item.TryGetProperty("reportedEvidence", out var reports) && !ValidateReports(reports)) return false;
            }
        }
        return true;
    }

    public static CollectionDataValidationResult<JsonDocument> ParseDocument(string? payload, int maximumBytes, bool versioned)
    {
        if (string.IsNullOrEmpty(payload)) return CollectionDataValidationResult<JsonDocument>.Invalid(CollectionDataValidationCode.NoPayload);
        JsonDocument? document = null;
        try
        {
            if (payload.Length > maximumBytes || StrictUtf8.GetByteCount(payload) > maximumBytes) return CollectionDataValidationResult<JsonDocument>.Invalid(CollectionDataValidationCode.PayloadTooLarge);
            document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 64, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            if (!UniqueProperties(document.RootElement)) { document.Dispose(); return CollectionDataValidationResult<JsonDocument>.Invalid(CollectionDataValidationCode.InvalidJson); }
            if (versioned)
            {
                var root = document.RootElement;
                JsonElement version = default;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out version) || version.ValueKind != JsonValueKind.String)
                { document.Dispose(); return CollectionDataValidationResult<JsonDocument>.Invalid(CollectionDataValidationCode.MissingSchemaVersion); }
                var text = version.GetString()!; var components = text.Split('.');
                if (components.Length != 2 || components.Any(c => c.Length == 0 || c.Any(ch => ch is < '0' or > '9') || !uint.TryParse(c, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
                { document.Dispose(); return CollectionDataValidationResult<JsonDocument>.Invalid(CollectionDataValidationCode.MalformedSchemaVersion); }
                if (text != "1.2") { document.Dispose(); return CollectionDataValidationResult<JsonDocument>.Invalid(CollectionDataValidationCode.UnsupportedSchemaVersion); }
            }
            return CollectionDataValidationResult<JsonDocument>.Valid(document);
        }
        catch (Exception e) when (e is JsonException or EncoderFallbackException or ArgumentException or InvalidOperationException)
        { document?.Dispose(); return CollectionDataValidationResult<JsonDocument>.Invalid(CollectionDataValidationCode.InvalidJson); }
    }

    private static CollectionDataValidationResult<T> Parse<T>(string? payload, int limit, string schema, JsonTypeInfo<T> type, Func<JsonElement, CollectionDataValidationCode> semantic)
    {
        var parsed = ParseDocument(payload, limit, true); if (!parsed.Success) return CollectionDataValidationResult<T>.Invalid(parsed.Code);
        using var document = parsed.Value!;
        try
        {
            var root = document.RootElement;
            if (!ContractSchemaRegistry.Default.Validate(root, schema)) return CollectionDataValidationResult<T>.Invalid(CollectionDataValidationCode.ContractBindingFailed);
            var result = semantic(root); if (result != CollectionDataValidationCode.None) return CollectionDataValidationResult<T>.Invalid(result);
            // All bound root schemas require an object; these source-generated DTOs have no custom converters.
            return CollectionDataValidationResult<T>.Valid(root.Deserialize(type)!);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or ArgumentException)
        { return CollectionDataValidationResult<T>.Invalid(CollectionDataValidationCode.ContractBindingFailed); }
    }

    private static bool UniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            return value.EnumerateObject().All(p => names.Add(p.Name) && UniqueProperties(p.Value));
        }
        return value.ValueKind != JsonValueKind.Array || value.EnumerateArray().All(UniqueProperties);
    }

    private static CollectionDataValidationCode Identity(JsonElement root, string? expected)
    {
        // common.contentId requires a string matching ^[1-9][0-9]{0,19}$; UInt64 overflow is semantic.
        var id = root.GetProperty("contentId").GetString();
        if (!ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out _)) return CollectionDataValidationCode.InvalidContentId;
        return expected is not null && id != expected ? CollectionDataValidationCode.WrongCharacter : CollectionDataValidationCode.None;
    }

    private static string DataType(string key) => UnlockKeys.Contains(key) ? "PermanentUnlock" : key == "reportedOwnership" ? "ReportedOwnership" : key is "outfits" or "relics" ? "Derived" : "CurrentItemPresence";
    private static bool Capability(string key, JsonElement value) => value.GetProperty("dataType").GetString() == DataType(key) && value.GetProperty("supportsQuantity").GetBoolean() == PresenceKeys.Take(5).Contains(key);
    private static bool Usable(JsonElement health) => health.GetProperty("sourceStatus").GetString() is "Fresh" or "Stale" && health.GetProperty("completeness").GetString() == "Complete" && health.GetProperty("capabilityState").GetString() != "UnavailablePendingValidation";
    private static bool ExactKeys(JsonElement map, IEnumerable<string> expected) => map.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal));
    private static bool ExactArray(JsonElement array, IEnumerable<string> expected) => array.EnumerateArray().Select(p => p.GetString()!).Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal));
    private static bool Sorted(string[] values) => values.SequenceEqual(values.Order(StringComparer.Ordinal)); // Notice arrays already require uniqueItems.

    private static bool ValidateMaps(JsonElement root, bool rich)
    {
        var caps = root.GetProperty("sourceCapabilities"); var health = root.GetProperty("sourceHealth");
        var keys = rich ? RichKeys.Concat(PresenceKeys).Append("reportedOwnership") : UnlockKeys.Concat(PresenceKeys).Append("reportedOwnership");
        if (!ExactKeys(caps, keys) || !ExactKeys(health, keys)) return false;
        foreach (var entry in health.EnumerateObject())
        {
            var cap = caps.GetProperty(entry.Name); var h = entry.Value;
            if (!Capability(entry.Name, cap) || h.GetProperty("dataType").GetString() != DataType(entry.Name) || h.GetProperty("capabilityState").GetString() != cap.GetProperty("capabilityState").GetString()) return false;
            if (entry.Name == "beasts" && !BeastHealth(h)) return false;
            if (h.GetProperty("captureGeneration").GetInt64() > root.GetProperty("captureGeneration").GetInt64()) return false;
            if (h.TryGetProperty("observedAtUtc", out var observed) && DateTimeOffset.Parse(observed.GetString()!, CultureInfo.InvariantCulture) > DateTimeOffset.Parse(root.GetProperty("capturedAtUtc").GetString()!, CultureInfo.InvariantCulture)) return false;
            if (root.GetProperty("consistency").GetString() == "Coherent" && Usable(h) && (h.GetProperty("captureGeneration").GetInt64() != root.GetProperty("captureGeneration").GetInt64() || h.GetProperty("sourceStatus").GetString() == "Stale")) return false;
            if (!rich && UnlockKeys.Contains(entry.Name))
            {
                var present = root.GetProperty("unlocks").TryGetProperty(entry.Name, out var set);
                if (present != Usable(h)) return false;
                if (present && !Ascending(set.GetProperty("unlockedIds"), null)) return false;
                if (present && entry.Name == "beasts" && set.GetProperty("unlockedIds").EnumerateArray().Any(id => !RecognizedBeasts.Value.Contains(id.GetUInt32()))) return false;
            }
            if (PresenceKeys.Contains(entry.Name))
            {
                var present = root.GetProperty("currentItemPresence").TryGetProperty(entry.Name, out var source);
                if (present != Usable(h)) return false;
                if (present && !Ascending(source.GetProperty("items"), "itemId")) return false;
                if (present && source.TryGetProperty("dresserEntries", out var entries) &&
                    (entry.Name != "glamourDresser" || !ValidateDresserEntries(entries, source.GetProperty("items")))) return false;
            }
        }
        if (root.GetProperty("currentItemPresence").EnumerateObject().Any(p => !PresenceKeys.Contains(p.Name))) return false;
        if (!rich && root.GetProperty("unlocks").EnumerateObject().Any(p => !UnlockKeys.Contains(p.Name))) return false;
        return true;
    }

    private static bool BeastHealth(JsonElement health)
    {
        var provenance = health.GetProperty("provenanceType").GetString();
        var status = health.GetProperty("sourceStatus").GetString();
        if (provenance is not ("None" or "Observed" or "Restored")) return false;
        if (status == "Fresh" && provenance != "Observed") return false;
        return status != "Stale" || (health.GetProperty("reasonCode").GetString() != "None" &&
            !string.IsNullOrWhiteSpace(health.GetProperty("reason").GetString()));
    }

    private static bool ValidateDresserEntries(JsonElement entries, JsonElement items)
    {
        if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > 800) return false;
        var componentIds = new HashSet<uint>();
        var previousEntry = -1;
        foreach (var entry in entries.EnumerateArray())
        {
            var index = entry.GetProperty("entryIndex").GetInt32();
            var raw = entry.GetProperty("rawItemId").GetUInt32();
            var id = entry.GetProperty("baseItemId").GetUInt32();
            var outfit = entry.GetProperty("outfitId");
            var components = entry.GetProperty("components");
            if (index <= previousEntry || index > 799 || raw % 1_000_000 != id || id == 0) return false;
            previousEntry = index;
            if (outfit.ValueKind == JsonValueKind.Null)
            {
                if (components.GetArrayLength() != 1 || components[0].GetProperty("slotIndex").GetInt32() != 0 || components[0].GetProperty("itemId").GetUInt32() != id) return false;
            }
            else if (outfit.GetUInt32() != id) return false;
            var previousSlot = -1;
            foreach (var component in components.EnumerateArray())
            {
                var slot = component.GetProperty("slotIndex").GetInt32();
                var item = component.GetProperty("itemId").GetUInt32();
                if (slot <= previousSlot || slot > 10 || item == 0) return false;
                previousSlot = slot;
                componentIds.Add(item);
            }
        }
        return componentIds.SetEquals(items.EnumerateArray().Select(item => item.GetProperty("itemId").GetUInt32()));
    }

    private static bool Ascending(JsonElement array, string? field)
    {
        uint previous = 0;
        foreach (var entry in array.EnumerateArray()) { var id = (field is null ? entry : entry.GetProperty(field)).GetUInt32(); if (id <= previous) return false; previous = id; }
        return true;
    }

    private static bool ValidateReports(JsonElement array)
    {
        uint previousId = 0; string? previousReporter = null;
        foreach (var report in array.EnumerateArray())
        {
            var id = report.GetProperty("itemId").GetUInt32(); var reporter = report.GetProperty("reporter").GetString()!;
            if (reporter != reporter.Trim().Normalize(NormalizationForm.FormC) || reporter.Any(char.IsControl) || StrictUtf8.GetByteCount(reporter) > 64 || id < previousId || (id == previousId && string.CompareOrdinal(previousReporter, reporter) >= 0)) return false;
            previousId = id; previousReporter = reporter;
        }
        return true;
    }
}
