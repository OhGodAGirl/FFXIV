using System.Collections.Immutable;
namespace Exportaramus.Features.CollectionData.Publication;

public sealed record AcceptedSnapshotBundle(CollectionData Data, long Revision, string CompactJson, string? RichJson,
    string Fingerprint, ImmutableDictionary<string, string> SourceFingerprints, PreparedFFXIVCollectExport Export,
    int HistoryCount, DateTimeOffset? LatestHistoryUtc)
{
    public ImmutableDictionary<string, string> DataFingerprints { get; init; } = ImmutableDictionary<string, string>.Empty;
    public ImmutableDictionary<string, int> CollectionOwnedCounts { get; init; } = ImmutableDictionary<string, int>.Empty;
    public ImmutableDictionary<string, int> ConfirmedPartialCounts { get; init; } = ImmutableDictionary<string, int>.Empty;
    public ImmutableDictionary<string, int> CollectionDeltas { get; init; } = ImmutableDictionary<string, int>.Empty;
    public string CompactJsonV11 { get; init; } = string.Empty;
    public string CompactJsonV12 { get; init; } = string.Empty;
    public string? RichJsonV12 { get; init; }
    public string CapabilitiesJsonV12 { get; init; } = string.Empty;
    public string UnavailableReportsCapabilitiesJsonV12 { get; init; } = string.Empty;
    public string CapabilitiesJsonV11 { get; init; } = string.Empty;
    public string UnavailableReportsCapabilitiesJsonV11 { get; init; } = string.Empty;
    public string CapabilitiesJson { get; init; } = string.Empty;
    public string UnavailableReportsCapabilitiesJson { get; init; } = string.Empty;
    public int CapabilitiesBytes { get; init; }
    public int CompactBytes { get; init; }
    public int RichBytes { get; init; }
    public int ExportBytes { get; init; }
}
