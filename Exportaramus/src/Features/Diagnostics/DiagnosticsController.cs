namespace Exportaramus.Features.Diagnostics;

/// <summary>Owns one read-only report worker. Its source supplies immutable redacted metadata, never live services.</summary>
public sealed class DiagnosticsController : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<DiagnosticReport>> source;
    private readonly DiagnosticMeasurements measurements;
    private readonly Action<string> copy;
    private CancellationTokenSource? cancellation;
    private Task<PreparedDiagnosticReport>? worker;
    private bool disposed;
    private long generation;
    private long workerGeneration;
    public DiagnosticsViewState View { get; private set; } = new(false, "Ready", "", null, false);
    public DiagnosticTask OwnedTask => new("Diagnostics", DiagnosticTaskPolicy.State(worker?.Status));
    public DiagnosticsController(Func<CancellationToken, Task<DiagnosticReport>> source, DiagnosticMeasurements measurements, Action<string> copy)
    { this.source = source; this.measurements = measurements; this.copy = copy; }
    public void Dispatch(DiagnosticsIntent intent)
    {
        if (disposed) return;
        switch (intent)
        {
            case DiagnosticsIntent.GenerateReport:
                if (worker is not null) return;
                cancellation = new(); workerGeneration = ++generation;
                var token = cancellation.Token;
                worker = Task.Run(async () =>
                {
                    using var timing = new DiagnosticTiming(measurements, "DiagnosticReport", true);
                    var report = await source(token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    return DiagnosticReportSerializer.Prepare(report);
                }, token);
                View = View with { Running = true, Status = "Running", Error = "" };
                break;
            case DiagnosticsIntent.CopyReport:
                if (View.Report is { } prepared) copy(prepared.Json);
                break;
            case DiagnosticsIntent.ResetMeasurements: measurements.ResetMeasurements(); break;
            case DiagnosticsIntent.Close:
                generation++; cancellation?.Cancel(); View = View with { Running = false, Status = View.Report?.Model.ReportStatus ?? "Ready" }; break;
        }
    }
    public void SetReduceMotion(bool enabled)
    {
        if (View.ReduceMotion != enabled) View = View with { ReduceMotion = enabled };
    }
    public void Update()
    {
        if (worker is null || !worker.IsCompleted) return;
        var completedWorker = worker;
        var completedCancellation = cancellation!;
        worker = null;
        cancellation = null;
        try
        {
            var prepared = completedWorker.GetAwaiter().GetResult();
            if (workerGeneration == generation) View = View with { Running = false, Status = prepared.Model.ReportStatus, Report = prepared };
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            measurements.Failure("Diagnostics", "GenerateReport", "ReportFailed", exception);
            if (workerGeneration == generation) View = View with { Running = false, Status = "Failed", Error = "Report generation failed. Generate a new report." };
        }
        finally { completedCancellation.Dispose(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true; generation++; cancellation?.Cancel();
        if (worker is not null)
        {
            try { await worker.ConfigureAwait(false); } catch (Exception) { }
        }
        worker = null; cancellation?.Dispose(); cancellation = null;
        View = View with { Running = false };
    }
}
