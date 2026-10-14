using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticStorage
{
    public bool CanonicalAvailable { get; init; }
    public int CanonicalBytes { get; init; }
    public int HistoryFileCount { get; init; }
    public long HistoryBytes { get; init; }
    public DiagnosticIoResult LastRead { get; init; } = new("Never", 0, 0, null);
    public DiagnosticIoResult LastWrite { get; init; } = new("Never", 0, 0, null);
    public string RecoveryState { get; init; } = "None";
}
