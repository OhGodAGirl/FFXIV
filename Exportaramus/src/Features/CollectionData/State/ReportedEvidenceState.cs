using System.Collections.Immutable;
namespace Exportaramus.Features.CollectionData.State;

public sealed record ReportedEvidence(uint ItemId, string Reporter, DateTimeOffset ReportedAtUtc);
public sealed record ReportedEvidenceState(ImmutableArray<ReportedEvidence> Items, CollectionSourceStatus Health);
