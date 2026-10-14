using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Exportaramus.ConsumerKit.V1;
using Exportaramus.Shared.Serialization;

namespace Exportaramus.Features.Diagnostics;

public static class DiagnosticReportSerializer
{
    public const int MaximumUtf8Bytes = 256 * 1024;
    private static readonly string[] ProhibitedNames = ["contentId", "characterName", "displayName", "world", "itemId", "itemIds", "quantity", "retainerId", "inventory", "environment", "commandLine", "clipboardText", "password", "token", "secret"];
    public static PreparedDiagnosticReport Prepare(DiagnosticReport report)
    {
        ValidateTypedStrings(report);
        if (report.ReportStatus == "Ready" && !report.IncompleteSections.IsEmpty || report.ReportStatus == "Partial" && report.IncompleteSections.IsEmpty)
            throw new InvalidDataException("DiagnosticSectionStatusInvalid");
        if (report.RecentFailures.Length > 20 || report.SourceHealth.Length > 64 || report.Lifecycle.OwnedTasks.Length > 32)
            throw new InvalidDataException("DiagnosticCapacityExceeded");
        var element = JsonSerializer.SerializeToElement(report, DiagnosticJsonContext.Default.DiagnosticReport);
        ValidateTree(element);
        var fingerprint = JsonOutput.Fingerprint(writer => Write(writer, element, true));
        report = report with { FingerprintSha256 = fingerprint };
        element = JsonSerializer.SerializeToElement(report, DiagnosticJsonContext.Default.DiagnosticReport);
        if (!ContractSchemaRegistry.Default.Validate(element, "exportaramus-diagnostics.schema.json"))
            throw new InvalidDataException("DiagnosticSchemaInvalid");
        var bytes = Canonical(element, false);
        if (bytes.Length > MaximumUtf8Bytes) throw new InvalidDataException("DiagnosticOutputSizeLimitExceeded");
        return new(report, Encoding.UTF8.GetString(bytes), bytes.Length, Display(element));
    }

    public static byte[] Canonical(JsonElement value, bool fingerprint) => JsonOutput.Build(writer => Write(writer, value, fingerprint));
    public static void ValidateWireJson(string json)
    {
        if (new UTF8Encoding(false, true).GetByteCount(json) > MaximumUtf8Bytes)
            throw new InvalidDataException("DiagnosticOutputSizeLimitExceeded");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        ValidateTree(document.RootElement);
        if (!ContractSchemaRegistry.Default.Validate(document.RootElement, "exportaramus-diagnostics.schema.json"))
            throw new InvalidDataException("DiagnosticSchemaInvalid");
        var status = document.RootElement.GetProperty("reportStatus").GetString();
        var incomplete = document.RootElement.GetProperty("incompleteSections").GetArrayLength();
        if (status == "Ready" && incomplete != 0 || status == "Partial" && incomplete == 0)
            throw new InvalidDataException("DiagnosticSectionStatusInvalid");
        var actual = document.RootElement.GetProperty("fingerprintSha256").GetString();
        var expected = JsonOutput.Fingerprint(writer => Write(writer, document.RootElement, true));
        if (actual != expected) throw new InvalidDataException("DiagnosticFingerprintMismatch");
    }
    private static void Write(Utf8JsonWriter writer, JsonElement value, bool omitRoot)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (omitRoot && property.Name is "generatedAtUtc" or "fingerprintSha256") continue;
                    writer.WritePropertyName(property.Name); Write(writer, property.Value, false);
                }
                writer.WriteEndObject(); break;
            case JsonValueKind.Array:
                writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) Write(writer, item, false); writer.WriteEndArray(); break;
            default: value.WriteTo(writer); break;
        }
    }
    private static void ValidateTree(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
            {
                if (ProhibitedNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("DiagnosticProhibitedField");
                ValidateTree(property.Value);
            }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) ValidateTree(item);
        else if (value.ValueKind == JsonValueKind.String && !DiagnosticRedactionPolicy.IsSafe(value.GetString()!))
            throw new InvalidDataException("DiagnosticProhibitedText");
    }
    private static void ValidateTypedStrings(DiagnosticReport report)
    {
        // All dynamic text is scanned before serializing the allowlisted report model.
        IEnumerable<string?> strings = [report.Format, report.SchemaVersion, report.ReportStatus, report.FingerprintSha256,
            report.Build.PluginVersion, report.Build.BuildConfiguration, report.Build.SourceRevision, report.Build.AssemblyInformationalVersion,
            report.Build.PackageSha256, report.Runtime.DalamudApiVersion, report.Runtime.DotnetRuntimeVersion, report.Runtime.OsVersion,
            report.Runtime.FfxivClientVersion, report.Runtime.GameDataVersion, report.Runtime.ClientLanguage, report.Lifecycle.State,
            report.Operations.ControllerState, report.Operations.CaptureType, report.Operations.PendingRequestPriority,
            report.CollectionPipeline.CatalogCache.KeyHash, report.CollectionPipeline.CatalogCache.Status,
            report.CollectionPipeline.Serializers.Compact, report.CollectionPipeline.Serializers.Compendium, report.CollectionPipeline.Serializers.FfxivCollect,
            report.CollectionPipeline.FfxivCollect.UpstreamCommit, report.CollectionPipeline.FfxivCollect.ContractSha256, report.CollectionPipeline.FfxivCollect.MappingSha256,
            report.Storage.LastRead.Status, report.Storage.LastRead.FailureCode, report.Storage.LastWrite.Status, report.Storage.LastWrite.FailureCode,
            report.Storage.RecoveryState, report.Ipc.LastWithdrawalReason, report.Redaction.SchemaVersion];
        strings = strings.Concat(report.IncompleteSections).Concat(report.Runtime.Dependencies.Values)
            .Concat(report.CollectionPipeline.Fingerprints.Values).Concat(report.Redaction.AppliedRules)
            .Concat(report.Lifecycle.OwnedTasks.SelectMany(x => new[] { x.Name, x.State }))
            .Concat(report.CollectionPipeline.Omissions.SelectMany(x => new[] { x.SourceKey, x.ReasonCode }))
            .Concat(report.SourceHealth.SelectMany(x => new[] { x.SourceKey, x.CapabilityState, x.SourceStatus, x.Completeness, x.ProvenanceType, x.ReasonCode }));
        if (strings.Any(value => value is not null && !DiagnosticRedactionPolicy.IsSafe(value)))
            throw new InvalidDataException("DiagnosticProhibitedText");
        foreach (var failure in report.RecentFailures)
        {
            var exception = failure.ExceptionType;
            if (!DiagnosticRedactionPolicy.IsSafe(failure.Message) || !DiagnosticRedactionPolicy.IsSymbol(failure.Component) ||
                !DiagnosticRedactionPolicy.IsSymbol(failure.Operation) || !DiagnosticRedactionPolicy.IsSymbol(failure.Code) ||
                exception is not null && !DiagnosticRedactionPolicy.IsSymbol(exception))
                throw new InvalidDataException("DiagnosticProhibitedText");
            foreach (var frame in failure.Frames)
            {
                var name = frame.SourceFileName;
                if (!DiagnosticRedactionPolicy.IsSafe(frame.Assembly) || !DiagnosticRedactionPolicy.IsSafe(frame.Type) ||
                    !DiagnosticRedactionPolicy.IsSafe(frame.Method) || name is not null && (!DiagnosticRedactionPolicy.IsSafe(name) || name.Contains('/') || name.Contains('\\')))
                    throw new InvalidDataException("DiagnosticProhibitedPath");
            }
        }
        foreach (var key in report.Runtime.Dependencies.Keys.Concat(report.Performance.Keys).Concat(report.CollectionPipeline.Fingerprints.Keys))
            if (!DiagnosticRedactionPolicy.IsSymbol(key) || ProhibitedNames.Contains(key, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("DiagnosticProhibitedField");
    }
    private static ImmutableArray<DiagnosticDisplaySection> Display(JsonElement root)
    {
        (string Title, bool Open, string[] Keys)[] sections =
        [
            ("Build and Runtime", false, ["build", "runtime"]), ("Lifecycle and Operations", true, ["lifecycle", "operations"]),
            ("Collection Pipeline", false, ["collectionPipeline"]), ("Source Health", false, ["sourceHealth"]),
            ("Storage and IPC", false, ["storage", "ipc"]), ("Performance", true, ["performance"]),
            ("UI", true, ["ui"]), ("Recent Failures", true, ["recentFailures"]), ("Redaction", false, ["redaction"]),
        ];
        return sections.Select(section => new DiagnosticDisplaySection(section.Title, section.Open,
            section.Keys.SelectMany(key => Flatten(root.GetProperty(key), key)).ToImmutableArray())).ToImmutableArray();
    }
    private static IEnumerable<DiagnosticDisplayRow> Flatten(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                foreach (var row in Flatten(property.Value, path + "." + property.Name)) yield return row;
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
                foreach (var row in Flatten(item, path + "[" + index++ + "]")) yield return row;
        }
        else yield return new(path, value.ValueKind == JsonValueKind.String ? value.GetString()! : value.ToString());
    }
}
