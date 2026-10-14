using System.Diagnostics;
using Exportaramus.Features.Diagnostics;
using Xunit.Abstractions;

namespace Exportaramus.Tests.Diagnostics;

[Collection(Exportaramus.Tests.Performance.OfflinePerformanceCollection.Name)]
public sealed class DiagnosticTimingPrecisionTests(ITestOutputHelper output)
{
    [Fact]
    public void AsyncMeasurementIncludesOtherThreadWorkWithoutUnusedAllocationContextBytes()
    {
        const int payloadSize = 1024;
        using var requested = new AutoResetEvent(false);
        using var completed = new AutoResetEvent(false);
        var stopping = false;
        byte[]? payload = null;
        var worker = new Thread(() =>
        {
            while (requested.WaitOne())
            {
                if (Volatile.Read(ref stopping)) return;
                payload = new byte[payloadSize];
                completed.Set();
            }
        });
        worker.Start();
        try
        {
            requested.Set(); Assert.True(completed.WaitOne(TimeSpan.FromSeconds(3)));
            var measurements = new DiagnosticMeasurements();
            _ = new DiagnosticTiming(measurements, "AsyncCapture", true).AllocatedBytes;

            var outerStart = GC.GetTotalAllocatedBytes(true);
            var timing = new DiagnosticTiming(measurements, "AsyncCapture", true);
            requested.Set(); var completedInTime = completed.WaitOne(TimeSpan.FromSeconds(3));
            var measured = timing.AllocatedBytes;
            var outerAllocated = GC.GetTotalAllocatedBytes(true) - outerStart;

            Assert.True(completedInTime);
            Assert.InRange(measured, payloadSize, outerAllocated);
            GC.KeepAlive(payload);
            output.WriteLine($"Known other-thread payload: {payloadSize}; measured process bytes: {measured}; independently bounded outer interval: {outerAllocated}.");
        }
        finally
        {
            Volatile.Write(ref stopping, true); requested.Set();
            Assert.True(worker.Join(TimeSpan.FromSeconds(3)));
        }
    }

    [Fact]
    public void PreciseBackgroundCounterPairFitsWithinTheReportConstructionBudget()
    {
        var measurements = new DiagnosticMeasurements();
        for (var i = 0; i < 10; i++) _ = new DiagnosticTiming(measurements, "AsyncCapture", true).AllocatedBytes;
        var durations = new double[100];

        for (var i = 0; i < durations.Length; i++)
        {
            var started = Stopwatch.GetTimestamp();
            _ = new DiagnosticTiming(measurements, "AsyncCapture", true).AllocatedBytes;
            durations[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }

        var distribution = DiagnosticMeasurements.Percentiles(durations);
        output.WriteLine($"Precise process counter pair milliseconds: p50 {distribution.P50:F4}; p95 {distribution.P95:F4}; max {distribution.Maximum:F4}.");
        Assert.True(distribution.P95 < 10, "Counter collection must consume less than ten percent of the 100 ms offline report construction budget.");
    }
}
