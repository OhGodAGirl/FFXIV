using Exportaramus.Features.Diagnostics;

namespace Exportaramus.Tests.Diagnostics;

[Collection(Exportaramus.Tests.Performance.OfflinePerformanceCollection.Name)]
public sealed class DiagnosticAsyncAllocationBoundaryTests
{
    [Fact]
    public async Task ActualControllerWorkerIncludesKnownOtherThreadAllocationBeforeItsSourceReturns()
    {
        const int payloadBytes = 2 * 1024 * 1024;
        using var requested = new ManualResetEvent(false);
        using var allocated = new ManualResetEvent(false);
        long actualAllocation = 0;
        var sourceThread = 0;
        var allocationThread = 0;
        var report = new DiagnosticReport { GeneratedAtUtc = new(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc) };
        // Warm preparation independently; the measured controller operation still prepares a new report.
        DiagnosticReportSerializer.Prepare(report);
        var allocator = new Thread(() =>
        {
            if (!requested.WaitOne(TimeSpan.FromSeconds(3))) return;
            allocationThread = Environment.CurrentManagedThreadId;
            var before = GC.GetAllocatedBytesForCurrentThread();
            var payload = new byte[payloadBytes];
            actualAllocation = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(payload);
            allocated.Set();
        });
        var measurements = new DiagnosticMeasurements();
        await using var controller = new DiagnosticsController(_ =>
        {
            sourceThread = Environment.CurrentManagedThreadId;
            requested.Set();
            if (!allocated.WaitOne(TimeSpan.FromSeconds(3))) throw new TimeoutException("The fixture allocator did not complete.");
            // A completed source task keeps both timing endpoints on this actual Task.Run worker.
            // Its thread-local counter cannot include the dedicated allocator's bytes.
            return Task.FromResult(report);
        }, measurements, _ => { });
        allocator.Start();
        try
        {
            controller.Dispatch(DiagnosticsIntent.GenerateReport);
            await DiagnosticWorkerTest.DrainAsync(controller);

            Assert.NotNull(controller.View.Report);
            Assert.NotEqual(sourceThread, allocationThread);
            Assert.True(actualAllocation >= payloadBytes);
            var sample = measurements.Snapshot()["DiagnosticReport"];
            Assert.Equal(1, sample.SampleCount);
            Assert.True(sample.AllocatedBytes.Maximum >= actualAllocation,
                $"The controller's asynchronous interval must include the known {actualAllocation} other-thread bytes; observed {sample.AllocatedBytes.Maximum}.");
        }
        finally
        {
            requested.Set();
            allocated.Set();
            Assert.True(allocator.Join(TimeSpan.FromSeconds(3)), "The owned fixture allocator must terminate.");
        }
    }

    [Fact]
    public async Task ReportWorkerAllocationIncludesConcurrentWorkDuringItsAsyncInterval()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<DiagnosticReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        var measurements = new DiagnosticMeasurements();
        await using var controller = new DiagnosticsController(_ => { entered.SetResult(); return release.Task; }, measurements, _ => { });
        const int ownedPayloadBytes = 512 * 1024;
        var allocator = new Thread(() =>
        {
            var bytes = new byte[ownedPayloadBytes];
            bytes[^1] = 1;
            GC.KeepAlive(bytes);
        });
        try
        {
            controller.Dispatch(DiagnosticsIntent.GenerateReport);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            allocator.Start();
            Assert.True(allocator.Join(TimeSpan.FromSeconds(3)));
            release.SetResult(new() { GeneratedAtUtc = new(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc) });
            await DiagnosticWorkerTest.DrainAsync(controller);
            Assert.NotNull(controller.View.Report);
            var reportMetric = Assert.Single(measurements.Snapshot());
            Assert.Equal("DiagnosticReport", reportMetric.Key);
            // This metric is documented as a process-wide upper bound, including allocations
            // made on other threads while its asynchronous operation remains outstanding.
            Assert.True(reportMetric.Value.AllocatedBytes.Maximum >= ownedPayloadBytes);
        }
        finally { release.TrySetResult(new() { GeneratedAtUtc = new(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc) }); }
    }
}
