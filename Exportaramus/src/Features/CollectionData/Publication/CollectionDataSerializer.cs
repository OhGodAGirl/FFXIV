using System.Text.Json;
using Exportaramus.Bootstrap;
using Exportaramus.Features.CollectionData.Contracts;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Shared.Serialization;
namespace Exportaramus.Features.CollectionData.Publication;

public static class CollectionDataSerializer
{
    public static string Compact(CollectionData data, long revision) => CompactCore(CollectionDataLegacyProjection.Data(data), revision, "1.0", CollectionDataLegacyProjection.CompactKeys);
    public static string CompactV11(CollectionData data, long revision) => CompactCore(CollectionDataLegacyProjection.DataV11(data), revision, "1.1", CollectionDataLegacyProjection.CompactKeysV11);
    public static string CompactV12(CollectionData data, long revision) => CompactCore(data, revision, "1.2", CollectionKeys.Compact);
    private static string CompactCore(CollectionData data, long revision, string version, IEnumerable<string> keys) => JsonOutput.Text(writer =>
    {
        writer.WriteStartObject();
        WriteCapture(writer, data.State);
        writer.WriteString("schemaVersion", version); writer.WriteString("providerInternalName", PluginIdentity.Name);
        writer.WriteString("providerVersion", PluginIdentity.Version); writer.WriteString("providerSessionId", data.State.ProviderSessionId);
        writer.WriteNumber("revision", revision); writer.WriteString("contentId", data.State.ContentId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.WriteString("consistency", Consistency(data));
        writer.WriteStartObject("unlocks");
        foreach (var pair in data.State.Unlocks.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!pair.Value.Health.IsUsable) continue;
            writer.WriteStartObject(pair.Key); writer.WriteStartArray("unlockedIds");
            foreach (var id in pair.Value.UnlockedIds) writer.WriteNumberValue(id);
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        writer.WriteEndObject(); Presence(writer, data, version != "1.0");
        writer.WriteStartObject("reportedOwnership"); Reports(writer, data.State.ReportedEvidence.Items); writer.WriteEndObject();
        Maps(writer, data, keys); writer.WriteEndObject();
    });

    public static string Rich(CollectionData data, string displayName, DateTimeOffset exportedAt) => RichCore(CollectionDataLegacyProjection.Data(data), displayName, exportedAt, false);
    public static string RichV12(CollectionData data, string displayName, DateTimeOffset exportedAt) => RichCore(CollectionDataLegacyProjection.RichV12(data), displayName, exportedAt, true);
    private static string RichCore(CollectionData data, string displayName, DateTimeOffset exportedAt, bool extended) => JsonOutput.Text(writer =>
    {
        writer.WriteStartObject(); writer.WriteString("format", "exportaramus-snapshot"); writer.WriteString("schemaVersion", extended ? "1.2" : "1.0");
        writer.WriteString("exportaramusVersion", PluginIdentity.Version); JsonOutput.Timestamp(writer, "exportedAtUtc", exportedAt);
        WriteCapture(writer, data.State); writer.WriteString("consistency", Consistency(data));
        writer.WriteStartObject("catalog"); writer.WriteString("clientLanguage", data.Catalog.ClientLanguage); writer.WriteString("gameDataVersion", data.Catalog.GameDataVersion); writer.WriteEndObject();
        writer.WriteStartObject("character"); writer.WriteString("displayName", displayName); writer.WriteEndObject();
        writer.WriteStartObject("collections");
        foreach (var pair in data.Collections.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var value = pair.Value; writer.WriteStartObject(pair.Key);
            writer.WriteString("label", value.Label); writer.WriteString("ownershipKind", value.OwnershipKind.ToString()); writer.WriteBoolean("totalKnown", value.TotalKnown);
            writer.WriteString("sourceStatus", value.Health.SourceStatus.ToString()); writer.WriteString("completeness", value.Health.Completeness.ToString());
            writer.WriteString("provenanceType", value.Health.ProvenanceType.ToString()); JsonOutput.Timestamp(writer, "observedAtUtc", value.Health.ObservedAtUtc!.Value);
            writer.WriteStartArray("items");
            foreach (var item in value.Items)
            {
                writer.WriteStartObject(); writer.WriteNumber("id", item.Id); writer.WriteString("name", item.Name); writer.WriteBoolean("owned", item.Owned);
                writer.WriteStartObject("ownershipEvidence"); writer.WriteString("ruleCode", item.OwnershipEvidence.RuleCode); writer.WriteStartArray("facts");
                foreach (var fact in item.OwnershipEvidence.Facts)
                {
                    writer.WriteStartObject(); writer.WriteString("dataType", fact.DataType.ToString()); writer.WriteString("sourceKey", fact.SourceKey);
                    writer.WriteNumber("subjectId", fact.SubjectId); writer.WriteString("state", fact.State); writer.WriteString("sourceStatus", fact.SourceStatus.ToString()); writer.WriteEndObject();
                }
                writer.WriteEndArray(); writer.WriteEndObject();
                var status = item.Status;
                var sequence = item.Sequence;
                if (status.HasValue) writer.WriteString("status", status.Value.ToString());
                if (sequence.HasValue) { writer.WriteStartObject("progress"); writer.WriteNumber("sequence", sequence.Value); writer.WriteEndObject(); }
                if (pair.Key == "achievements") writer.WriteNull("rewardCollected");
                writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        writer.WriteEndObject(); Presence(writer, data, extended);
        Maps(writer, data, extended ? CollectionKeys.All : CollectionDataLegacyProjection.CompactKeys.Concat(CollectionKeys.RichOnly));
        writer.WriteStartObject("omittedCollections");
        var unlockKeys = extended ? CollectionKeys.Unlocks : Exportaramus.ConsumerKit.V1.CollectionDataSemanticValidator.UnlockKeys;
        foreach (var key in unlockKeys.Concat(CollectionKeys.RichOnly).Order(StringComparer.Ordinal))
        {
            if (data.Collections.ContainsKey(key)) continue;
            writer.WriteStartObject(key); writer.WriteString("reasonCode", data.SourceHealth[key].ReasonCode.ToString());
            writer.WriteString("message", string.IsNullOrEmpty(data.SourceHealth[key].Reason) ? "Collection data is unavailable." : data.SourceHealth[key].Reason); writer.WriteEndObject();
        }
        writer.WriteEndObject(); writer.WriteEndObject();
    });

    public static void Reports(Utf8JsonWriter writer, IEnumerable<ReportedEvidence> reports)
    {
        writer.WriteStartArray("items");
        foreach (var report in reports.OrderBy(report => report.ItemId).ThenBy(report => report.Reporter, StringComparer.Ordinal))
        {
            writer.WriteStartObject(); writer.WriteNumber("itemId", report.ItemId); JsonOutput.Timestamp(writer, "reportedAtUtc", report.ReportedAtUtc); writer.WriteString("reporter", report.Reporter); writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
    public static string Consistency(CollectionData data) => data.SourceHealth.Values.Where(health => health.IsUsable)
        .All(health => health.CaptureGeneration == data.State.CaptureGeneration && health.SourceStatus == SourceStatus.Fresh)
        && data.State.CaptureType == CaptureType.Full ? "Coherent" : "MixedObservationTimes";

    public static void Capabilities(Utf8JsonWriter writer, CollectionData data, IEnumerable<string> keys)
    {
        writer.WriteStartObject("sourceCapabilities");
        foreach (var key in keys.Order(StringComparer.Ordinal))
        {
            var health = data.SourceHealth[key]; writer.WriteStartObject(key);
            writer.WriteString("dataType", health.DataType.ToString()); writer.WriteString("capabilityState", health.CapabilityState.ToString());
            writer.WriteBoolean("supportsQuantity", CollectionKeys.SupportsQuantity(key)); writer.WriteString("reasonCode", health.ReasonCode.ToString()); writer.WriteString("reason", health.Reason);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }
    private static void Maps(Utf8JsonWriter writer, CollectionData data, IEnumerable<string> keys)
    {
        Capabilities(writer, data, keys); writer.WriteStartObject("sourceHealth");
        foreach (var key in keys.Order(StringComparer.Ordinal))
        {
            var health = data.SourceHealth[key]; writer.WriteStartObject(key);
            writer.WriteString("dataType", health.DataType.ToString()); writer.WriteString("capabilityState", health.CapabilityState.ToString());
            writer.WriteString("sourceStatus", health.SourceStatus.ToString()); writer.WriteString("completeness", health.Completeness.ToString());
            writer.WriteString("provenanceType", health.ProvenanceType.ToString()); writer.WriteNumber("captureGeneration", health.CaptureGeneration);
            if (health.ObservedAtUtc is { } observed) JsonOutput.Timestamp(writer, "observedAtUtc", observed);
            writer.WriteString("reasonCode", health.ReasonCode.ToString()); writer.WriteString("reason", health.Reason); writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }
    private static void Presence(Utf8JsonWriter writer, CollectionData data, bool extended = false)
    {
        writer.WriteStartObject("currentItemPresence");
        foreach (var pair in data.State.CurrentItemPresence.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!pair.Value.Health.IsUsable) continue;
            writer.WriteStartObject(pair.Key); writer.WriteStartArray("items");
            foreach (var item in pair.Value.Items)
            {
                writer.WriteStartObject(); writer.WriteNumber("itemId", item.ItemId);
                if (item.Quantity is { } quantity) writer.WriteNumber("quantity", quantity);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            if (extended && pair.Key == "glamourDresser" && pair.Value.DresserEntries is { } entries)
            {
                writer.WriteStartArray("dresserEntries");
                foreach (var entry in entries)
                {
                    writer.WriteStartObject(); writer.WriteNumber("entryIndex", entry.EntryIndex);
                    writer.WriteNumber("rawItemId", entry.RawItemId); writer.WriteNumber("baseItemId", entry.BaseItemId);
                    if (entry.OutfitId is { } outfit) writer.WriteNumber("outfitId", outfit); else writer.WriteNull("outfitId");
                    writer.WriteStartArray("components");
                    foreach (var component in entry.Components)
                    {
                        writer.WriteStartObject(); writer.WriteNumber("slotIndex", component.SlotIndex);
                        writer.WriteNumber("itemId", component.ItemId); writer.WriteEndObject();
                    }
                    writer.WriteEndArray(); writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }
    private static void WriteCapture(Utf8JsonWriter writer, CollectionState state)
    {
        JsonOutput.Timestamp(writer, "capturedAtUtc", state.CapturedAtUtc); writer.WriteString("captureType", state.CaptureType.ToString()); writer.WriteNumber("captureGeneration", state.CaptureGeneration);
    }
}

