using Exportaramus.Bootstrap;
using Exportaramus.ConsumerKit.V1;
using Exportaramus.Features.CollectionData.Contracts;
using Exportaramus.Shared.Serialization;
namespace Exportaramus.Features.CollectionData.Publication;

public static class CapabilityDocumentWriter
{
    public static string Build(CollectionData data, bool reportsAvailable) => BuildCore(data, reportsAvailable, "1.0");
    public static string BuildV11(CollectionData data, bool reportsAvailable) => BuildCore(data, reportsAvailable, "1.1");
    public static string BuildV12(CollectionData data, bool reportsAvailable) => BuildCore(data, reportsAvailable, "1.2");
    private static string BuildCore(CollectionData data, bool reportsAvailable, string version) => JsonOutput.Text(writer =>
    {
        writer.WriteStartObject(); writer.WriteString("schemaVersion", version); writer.WriteString("providerInternalName", PluginIdentity.Name);
        writer.WriteString("providerVersion", PluginIdentity.Version); writer.WriteString("providerSessionId", data.State.ProviderSessionId);
        writer.WriteString("clientLanguage", data.Catalog.ClientLanguage); writer.WriteString("gameDataVersion", data.Catalog.GameDataVersion);
        writer.WriteStartObject("gates");
        foreach (var name in new[] { "GetCapabilities", "GetSnapshot", "SnapshotChanged", "SnapshotWithdrawn", "ReportOwnershipEvidence" })
            writer.WriteString(char.ToLowerInvariant(name[0]) + name[1..], (version == "1.0" ? "Exportaramus.CollectionData." : "Exportaramus.CollectionData.V" + version.Replace('.', '_') + ".") + name);
        writer.WriteEndObject(); writer.WriteStartArray("unlockKeys"); foreach (var key in version == "1.2" ? CollectionKeys.Unlocks : version == "1.1" ? Exportaramus.ConsumerKit.V1_1.CollectionDataSemanticValidator.UnlockKeys : CollectionDataSemanticValidator.UnlockKeys) writer.WriteStringValue(key); writer.WriteEndArray();
        writer.WriteStartArray("currentPresenceSources"); foreach (var key in CollectionKeys.Presence) writer.WriteStringValue(key); writer.WriteEndArray();
        CollectionDataSerializer.Capabilities(writer, data, version == "1.2" ? CollectionKeys.Compact : version == "1.1" ? CollectionDataLegacyProjection.CompactKeysV11 : CollectionDataLegacyProjection.CompactKeys);
        writer.WriteStartObject("collectionDataSchema"); writer.WriteString("id", $"https://exportaramus.local/schemas/{version}/collection-data.schema.json");
        writer.WriteString("sha256", version == "1.2" ? Exportaramus.ConsumerKit.V1_2.CollectionDataSemanticValidator.CompactSchemaHash : version == "1.1" ? Exportaramus.ConsumerKit.V1_1.CollectionDataSemanticValidator.CompactSchemaHash : CollectionDataSemanticValidator.CompactSchemaHash); writer.WriteEndObject();
        writer.WriteStartObject("limits");
        foreach (var (key, value) in new (string, long)[] { ("snapshotUtf8Bytes", 4194304), ("capabilitiesUtf8Bytes", 16384), ("eventUtf8Bytes", 4096), ("idsPerCollection", 50000), ("reportedEvidenceRecordsPerCharacter", 10000), ("reporterUtf8Bytes", 64), ("pendingReportsTotal", 256), ("pendingReportsPerReporter", 32), ("quantityMaximum", uint.MaxValue) }) writer.WriteNumber(key, value);
        writer.WriteStartObject("globalReportRate"); writer.WriteNumber("acceptedPerMinute", 60); writer.WriteNumber("burst", 20); writer.WriteEndObject();
        writer.WriteStartObject("reporterReportRate"); writer.WriteNumber("acceptedPerMinute", 30); writer.WriteNumber("burst", 10); writer.WriteEndObject(); writer.WriteEndObject();
        writer.WriteStartObject("reportedOwnershipEvidence"); writer.WriteBoolean("available", reportsAvailable); writer.WriteStartArray("resultCodes");
        foreach (var code in CollectionDataSemanticValidator.ReportCodes) writer.WriteStringValue(code);
        writer.WriteEndArray(); writer.WriteEndObject(); writer.WriteEndObject();
    });
}
