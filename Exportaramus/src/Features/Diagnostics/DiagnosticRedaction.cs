using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticRedaction
{
    public string SchemaVersion { get; init; } = "1.0";
    public ImmutableArray<string> AppliedRules { get; init; } = ["TypedAllowlist", "DiscardRawFailureText", "ProhibitedDataScan"];
    public long DroppedFieldCount { get; init; }
    public long TruncatedFieldCount { get; init; }
    public DiagnosticProhibitedData ProhibitedDataAssertions { get; init; } = new();
}
