using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticIpc
{
    public DiagnosticGates GatesRegistered { get; init; } = new();
    public DiagnosticPayloadBytes PayloadBytes { get; init; } = new(0, 0, 0);
    public bool ChangedNoticePending { get; init; }
    public DiagnosticQueue ReportQueue { get; init; } = new(0, 256);
    public DiagnosticCounters ReportCounters { get; init; } = new(0, 0, 0);
    public string? LastWithdrawalReason { get; init; }
}
