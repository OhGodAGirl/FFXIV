using System.Text.Json;
using System.Text.Json.Serialization;

namespace EZDelivery.Bootstrap;

/// <summary>Prepared diagnostic snapshot; serialization performs no native reads.</summary>
public sealed record DiagnosticReport(string SchemaVersion, DateTimeOffset GeneratedAtUtc, string PluginVersion,
    bool NativeBuildSupported, string NativeStatus, string DeliveryPhase, string DeliveryReason,
    int ConfirmedDeliveries, string ShoppingStatus, int ConfirmedPurchases,
    string CollectionStatus, string? IncompleteSource, string AcquisitionReport,
    IReadOnlyList<string> Failures, long FrameworkSamples, double MeanFrameworkMilliseconds, double MaximumFrameworkMilliseconds)
{
    // Generated serialization avoids reflection-based property traversal on each report.
    /// <summary>Most recent delivery dialog correlation diagnostic.</summary>
    public string DeliveryDialogCheck { get; init; } = "Not observed";
    /// <summary>Module build identifier used to distinguish loaded candidates.</summary>
    public string LoadedBuildId { get; init; } = "Unknown";
    /// <summary>UTC time associated with the delivery dialog diagnostic.</summary>
    public DateTimeOffset? DeliveryDialogCheckedAtUtc { get; init; }
    /// <summary>Most recent purchase confirmation correlation diagnostic.</summary>
    public string ShoppingDialogCheck { get; init; } = "Not observed";
    /// <summary>Most recent delivery source-mapping diagnostic.</summary>
    public string DeliveryMappingCheck { get; init; } = "Not observed";
    /// <summary>Most recent delivery list-refresh diagnostic.</summary>
    public string DeliveryRefreshCheck { get; init; } = "Not observed";
    /// <summary>UTC time of the last observed native list refresh.</summary>
    public DateTimeOffset? LastNativeDeliveryRefreshAtUtc { get; init; }
    /// <summary>Serializes this prepared snapshot with the generated JSON contract.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, DiagnosticJsonContext.Default.DiagnosticReport);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(DiagnosticReport))]
internal partial class DiagnosticJsonContext : JsonSerializerContext { }
