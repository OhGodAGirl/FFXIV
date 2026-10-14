using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Contracts;
using Exportaramus.Shared.Serialization;
namespace Exportaramus.Features.CollectionData.Publication;

public sealed class ChangedNoticeCoalescer
{
    private readonly SortedSet<string> types = new(StringComparer.Ordinal);
    private readonly SortedSet<string> sources = new(StringComparer.Ordinal);
    private AcceptedSnapshotBundle? newest;
    public void Add(AcceptedSnapshotBundle value, AcceptedSnapshotBundle? previous, IEnumerable<string> observedSources)
    {
        if (newest is not null && newest.Data.State.ContentId != value.Data.State.ContentId) Clear();
        newest = value;
        foreach (var pair in value.SourceFingerprints)
        {
            if (!CollectionKeys.Compact.Contains(pair.Key)) continue;
            if (previous?.SourceFingerprints.GetValueOrDefault(pair.Key) == pair.Value) continue;
            sources.Add(pair.Key);
            if (previous is null || previous.DataFingerprints.GetValueOrDefault(pair.Key) != value.DataFingerprints.GetValueOrDefault(pair.Key))
                types.Add(CollectionKeys.Unlocks.Contains(pair.Key) ? "Unlocks" : CollectionKeys.Presence.Contains(pair.Key) ? "CurrentItemPresence" : "ReportedOwnership");
            types.Add("SourceHealth");
            if (previous is null || previous.Data.SourceHealth[pair.Key].CapabilityState != value.Data.SourceHealth[pair.Key].CapabilityState)
                types.Add("SourceCapabilities");
        }
        foreach (var key in observedSources.Where(CollectionKeys.Compact.Contains)) sources.Add(key);
        types.Add("SourceHealth");
    }
    public string? Take(bool extended = false) => TakeCore(extended ? "1.1" : "1.0");
    public string? TakeV12() => TakeCore("1.2");
    private string? TakeCore(string version)
    {
        int sourceCount = sources.Count;
        if (newest is null || sourceCount == 0) return null;
        var value = newest;
        var includedSources = sources.Where(key => (version == "1.2" || key != "beasts") && (version != "1.0" || key != "facewear")).ToArray();
        if (includedSources.Length == 0) { Clear(); return null; }
        var result = JsonOutput.Text(writer =>
        {
            writer.WriteStartObject(); writer.WriteString("eventType", "SnapshotChanged"); writer.WriteString("schemaVersion", version);
            writer.WriteString("providerSessionId", value.Data.State.ProviderSessionId); writer.WriteNumber("revision", value.Revision);
            writer.WriteString("contentId", value.Data.State.ContentId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            JsonOutput.Timestamp(writer, "capturedAtUtc", value.Data.State.CapturedAtUtc);
            writer.WriteStartArray("changedDataTypes"); foreach (var type in types) writer.WriteStringValue(type); writer.WriteEndArray();
            writer.WriteStartArray("changedSources"); foreach (var source in includedSources) writer.WriteStringValue(source); writer.WriteEndArray(); writer.WriteEndObject();
        });
        Clear(); return result;
    }
    public void Clear() { newest = null; types.Clear(); sources.Clear(); }
}
