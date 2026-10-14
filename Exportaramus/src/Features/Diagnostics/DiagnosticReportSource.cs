namespace Exportaramus.Features.Diagnostics;

public sealed class DiagnosticReportSource(DiagnosticMeasurements measurements)
{
    private DiagnosticReport? current;
    public void Set(DiagnosticReport value) => Volatile.Write(ref current, value);
    public Task<DiagnosticReport> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = Volatile.Read(ref current) ?? throw new InvalidOperationException("Verified diagnostic metadata is unavailable.");
        return Task.FromResult(snapshot with { GeneratedAtUtc = DateTime.UtcNow, Performance = measurements.Snapshot(), RecentFailures = measurements.Failures });
    }
}
