using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticOperations
{
    public string ControllerState { get; init; } = "Idle";
    public string? CaptureType { get; init; }
    public long CaptureGeneration { get; init; }
    public double ElapsedMilliseconds { get; init; }
    public DateTime? DeadlineAtUtc { get; init; }
    public int RetryCount { get; init; }
    public string PendingRequestPriority { get; init; } = "None";
    public bool StaleCompletionRejected { get; init; }
}
