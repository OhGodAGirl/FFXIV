using System.Collections.Immutable;
using Exportaramus.Shared.Serialization;
namespace Exportaramus.Features.CollectionData.Publication;

public static class CollectionFingerprint
{
    public static ImmutableDictionary<string, string> Sources(CollectionData data, bool includeHealth = true) => data.SourceHealth.Keys.ToImmutableDictionary(key => key, key =>
        JsonOutput.Fingerprint(writer =>
        {
            var health = data.SourceHealth[key]; writer.WriteStartObject();
            if (includeHealth) { writer.WriteString("capabilityState", health.CapabilityState.ToString()); writer.WriteString("completeness", health.Completeness.ToString()); }
            writer.WriteStartArray("ids");
            if (data.State.Unlocks.TryGetValue(key, out var unlock)) foreach (var id in unlock.UnlockedIds) writer.WriteNumberValue(id);
            if (data.Collections.TryGetValue(key, out var collection)) foreach (var item in collection.Items)
            {
                writer.WriteStartObject(); writer.WriteNumber("id", item.Id); writer.WriteBoolean("owned", item.Owned);
                writer.WriteStartObject("ownershipEvidence"); writer.WriteStartArray("facts");
                foreach (var fact in item.OwnershipEvidence.Facts)
                {
                    writer.WriteStartObject(); writer.WriteString("dataType", fact.DataType.ToString()); writer.WriteString("sourceKey", fact.SourceKey);
                    if (includeHealth) writer.WriteString("sourceStatus", fact.SourceStatus.ToString());
                    writer.WriteString("state", fact.State); writer.WriteNumber("subjectId", fact.SubjectId); writer.WriteEndObject();
                }
                writer.WriteEndArray(); writer.WriteString("ruleCode", item.OwnershipEvidence.RuleCode); writer.WriteEndObject();
                if (item.Sequence is { } sequence) writer.WriteNumber("sequence", sequence);
                if (item.Status is { } status) writer.WriteString("status", status.ToString()); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteStartArray("items");
            if (data.State.CurrentItemPresence.TryGetValue(key, out var source)) foreach (var item in source.Items)
            {
                writer.WriteStartObject(); writer.WriteNumber("id", item.ItemId); if (item.Quantity is { } quantity) writer.WriteNumber("quantity", quantity); writer.WriteEndObject();
            }
            if (key == "reportedOwnership") foreach (var report in data.State.ReportedEvidence.Items)
            {
                writer.WriteStartObject(); writer.WriteNumber("id", report.ItemId); writer.WriteString("reporter", report.Reporter); writer.WriteEndObject();
            }
            writer.WriteEndArray();
            if (key == "glamourDresser")
            {
                writer.WritePropertyName("dresserEntries");
                System.Text.Json.JsonSerializer.Serialize(writer, source?.DresserEntries);
            }
            if (includeHealth) { writer.WriteString("reasonCode", health.ReasonCode.ToString()); writer.WriteString("sourceStatus", health.SourceStatus.ToString()); }
            writer.WriteEndObject();
        }), StringComparer.Ordinal);

    public static string Combined(ImmutableDictionary<string, string> fingerprints) => JsonOutput.Fingerprint(writer =>
    {
        writer.WriteStartObject(); foreach (var pair in fingerprints.OrderBy(pair => pair.Key, StringComparer.Ordinal)) writer.WriteString(pair.Key, pair.Value); writer.WriteEndObject();
    });
}
