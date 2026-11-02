using System.Globalization;

namespace SpringCleaning.Shared.CollectionData;

internal sealed record CollectionDataSourceDiagnostic(
    CollectionSourceStatus Status,
    int ItemCount,
    DateTimeOffset? ObservedAtUtc,
    string? Reason)
{
    internal static readonly CollectionDataSourceDiagnostic Unavailable = new(
                CollectionSourceStatus.Unavailable,
                0,
                null,
                null);

    internal string ToReportLine(string label)
    {
        var line = $"{label}: {Status}; {ItemCount.ToString("N0", CultureInfo.InvariantCulture)} items";
        if (ObservedAtUtc.HasValue)
            line += $"; observed {ObservedAtUtc.Value:O}";
        if (!string.IsNullOrWhiteSpace(Reason))
            line += $"; reason {Reason}";
        return line;
    }
}
