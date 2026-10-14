using System.Collections.Immutable;
using Exportaramus.Features.Diagnostics;

namespace Exportaramus.Tests.Performance;

[Collection(OfflinePerformanceCollection.Name)]
public sealed class DiagnosticReportPerformanceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static DiagnosticReport Report => new() { GeneratedAtUtc = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc) };
    [Fact]
    public void RepresentativeBoundedReportMeetsOfflineConstructionBudgetAfterWarmup()
    {
        var measurements = new DiagnosticMeasurements();
        for (var metric = 0; metric < 16; metric++)
            for (var sample = 0; sample < 256; sample++) measurements.Record("Metric" + metric, sample, sample * 4);
        var report = Report with
        {
            Performance = measurements.Snapshot(),
            SourceHealth = Enumerable.Range(0, 31).Select(index => new DiagnosticSource("Source" + index, "UnavailablePendingValidation", "NeverObserved", "Unknown", "None", null, 0, "PatchValidationRequired")).ToImmutableArray(),
            RecentFailures = Enumerable.Range(0, 20).Select(index => DiagnosticRedactionPolicy.Failure(Report.GeneratedAtUtc.AddSeconds(index), "Storage", "Write", "WriteFailed", "IOException")).ToImmutableArray(),
        };
        for (var warmup = 0; warmup < 5; warmup++) DiagnosticReportSerializer.Prepare(report);
        var times = new List<double>();
        for (var sample = 0; sample < 20; sample++)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew(); var prepared = DiagnosticReportSerializer.Prepare(report); timer.Stop();
            times.Add(timer.Elapsed.TotalMilliseconds); Assert.True(prepared.Utf8Bytes <= DiagnosticReportSerializer.MaximumUtf8Bytes);
        }
        var percentiles = DiagnosticMeasurements.Percentiles(times);
        output.WriteLine($"Offline report preparation: samples={times.Count}; p95={percentiles.P95:F4} ms; max={percentiles.Maximum:F4} ms.");
        Assert.True(percentiles.P95 < 100, $"Offline report generation p95 must remain below 100 ms; observed p95={percentiles.P95:F4} ms, max={percentiles.Maximum:F4} ms.");
    }
}
