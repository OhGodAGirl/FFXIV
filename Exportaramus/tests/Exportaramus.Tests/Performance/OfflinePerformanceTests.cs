using System.Collections.Immutable;
using System.Diagnostics;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.Diagnostics;
using Exportaramus.Features.GuidedExport;
using Exportaramus.Features.Main;
using Exportaramus.Tests.Snapshots;
using Exportaramus.Shared.Ui;
using Xunit.Abstractions;

namespace Exportaramus.Tests.Performance;

/// <summary>Offline managed paths only. These measurements do not establish live Draw/framework/capture budgets.</summary>
[Collection(OfflinePerformanceCollection.Name)]
public sealed class OfflinePerformanceTests(ITestOutputHelper output)
{
    private static AcceptedSnapshotBundle Bundle(int payloadBytes)
    {
        var data = SnapshotFixture.Create();
        return new(data, 1, new string('x', payloadBytes), null, "hash", ImmutableDictionary<string, string>.Empty,
            PreparedFFXIVCollectExport.Unavailable("Pending"), 25, SnapshotFixture.Now)
        { CollectionOwnedCounts = data.Collections.ToImmutableDictionary(x => x.Key, _ => 1), CompactBytes = payloadBytes };
    }

    [Theory]
    [InlineData(128)]
    [InlineData(4 * 1024 * 1024)]
    public void CachedIpcGettersReturnIdenticalStringsWithoutPayloadAllocation(int payloadBytes)
    {
        var bundle = Bundle(payloadBytes); var store = new CollectionDataPublicationStore();
        var capabilities = new string('c', 16 * 1024); store.Accept(bundle); store.SetCapabilities(capabilities);
        var samples = new double[100]; long allocation = 0;
        // A dedicated thread keeps the measured interval outside the test runner's worker and
        // execution context. Thread setup, warmup and result reporting are outside each sample.
        var measurementThread = new Thread(() =>
        {
            for (var i = 0; i < 100; i++) _ = MeasureGetterBlock(store);
            for (var sample = 0; sample < samples.Length; sample++)
            {
                var measured = MeasureGetterBlock(store); samples[sample] = measured.Milliseconds; allocation += measured.Bytes;
            }
        });
        using (ExecutionContext.SuppressFlow()) measurementThread.Start();
        measurementThread.Join();
        Array.Sort(samples); output.WriteLine($"Offline cached getter pair p95={samples[94]:F6} ms; allocated={allocation} bytes; payload={payloadBytes} bytes.");
        Assert.Same(bundle.CompactJson, store.GetSnapshot()); Assert.Same(capabilities, store.GetCapabilities());
        Assert.Equal(0, allocation); Assert.True(samples[94] < .10);
    }
    private static (double Milliseconds, long Bytes) MeasureGetterBlock(CollectionDataPublicationStore store)
    {
        var start = Stopwatch.GetTimestamp(); var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) { _ = store.GetSnapshot(); _ = store.GetCapabilities(); }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        return (Stopwatch.GetElapsedTime(start).TotalMilliseconds / 1000, bytes);
    }

    [Fact]
    public void RepeatedLayoutAndPreparedDisplayReadsAllocateNothing()
    {
        var state = MainSnapshotPresenter.Prepare(Bundle(128), true, null, false, false, new GuidedExportController().View, SnapshotFixture.Now, true);
        var report = DiagnosticReportSerializer.Prepare(new() { GeneratedAtUtc = SnapshotFixture.Now.UtcDateTime });
        var section = report.Sections[0]; long allocation = 0;
        var measurementThread = new Thread(() =>
        {
            for (var i = 0; i < 100; i++) _ = MeasurePreparedReads(state, report, section);
            for (var i = 0; i < 100; i++) allocation += MeasurePreparedReads(state, report, section);
        });
        using (ExecutionContext.SuppressFlow()) measurementThread.Start();
        measurementThread.Join();
        output.WriteLine($"Offline layout/prepared display reads allocated={allocation} bytes over 100,000 iterations.");
        Assert.Equal(0, allocation);
    }

    private static long MeasurePreparedReads(MainViewState state, PreparedDiagnosticReport report, DiagnosticDisplaySection section)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            _ = LayoutPolicy.At(740); _ = state.CollectionCountText; _ = state.HistoryCountText; _ = state.LatestSnapshotText;
            _ = report.GeneratedText; _ = report.SizeText; _ = report.FingerprintText; _ = section.TableId;
        }
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void BoundedPresentationPreparationMeetsOfflinePointOneMillisecondBudget()
    {
        var bundle = Bundle(4 * 1024 * 1024); var guided = new GuidedExportController().View;
        MainViewState Prepare() => MainSnapshotPresenter.Prepare(bundle, true, null, false, false, guided, SnapshotFixture.Now, true);
        for (var i = 0; i < 1000; i++) _ = Prepare();
        var samples = new double[100]; var before = GC.GetAllocatedBytesForCurrentThread();
        for (var sample = 0; sample < samples.Length; sample++)
        {
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < 100; i++) _ = Prepare();
            samples[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / 100;
        }
        var perPreparation = (GC.GetAllocatedBytesForCurrentThread() - before) / 10000;
        Array.Sort(samples); output.WriteLine($"Offline preparation p95={samples[94]:F6} ms; allocated={perPreparation} bytes/preparation. Preparation occurs on invalidation, not every frame.");
        Assert.Equal(23, Prepare().Rows.Length); Assert.True(samples[94] < .10); Assert.True(perPreparation < 32 * 1024);
    }

    [Fact]
    public void ReportAcceptanceAndRejectionStayBoundedWithoutIo()
    {
        var queue = new ReportedEvidenceQueue(); queue.SetScope(123, [100], []);
        for (var i = 0; i < 1000; i++) queue.TryEnqueue("123", 100, "Offline fixture", i * 2, SnapshotFixture.Now);
        var samples = new double[100];
        for (var sample = 0; sample < samples.Length; sample++)
        {
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < 100; i++)
            {
                Assert.Equal("Accepted", queue.TryEnqueue("123", 100, "Offline fixture", (1000 + sample * 100 + i) * 2, SnapshotFixture.Now));
                Assert.Equal("WrongCharacter", queue.TryEnqueue("124", 100, "Offline fixture", (1000 + sample * 100 + i) * 2, SnapshotFixture.Now));
            }
            samples[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / 200;
        }
        Array.Sort(samples); output.WriteLine($"Offline report gate p95={samples[94]:F6} ms; pending={queue.Count}.");
        Assert.Equal(1, queue.Count); Assert.True(samples[94] < .10);
    }

    [Fact]
    public async Task IdleDiagnosticAndPresentationControllersAllocateNothingAfterWarmup()
    {
        var policy = new PresentationInvalidationPolicy(); policy.ShouldRefresh(1, 0, false, GuidedState.Idle);
        await using var diagnostics = new DiagnosticsController(_ => throw new InvalidOperationException("Idle must not generate a report."), new(), _ => throw new InvalidOperationException("Idle must not copy."));
        for (var i = 0; i < 1000; i++) { diagnostics.Update(); diagnostics.SetReduceMotion(false); policy.ShouldRefresh(1, 0, false, GuidedState.Idle); }
        var same = diagnostics.View; var before = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < 10000; i++) { diagnostics.Update(); diagnostics.SetReduceMotion(false); policy.ShouldRefresh(1, 0, false, GuidedState.Idle); }
        var milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds / 10000;
        var allocation = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"Offline idle policy/controller average={milliseconds:F6} ms; allocated={allocation} bytes.");
        Assert.Same(same, diagnostics.View); Assert.Equal(0, allocation); Assert.True(milliseconds < .10);
        diagnostics.SetReduceMotion(true); Assert.True(diagnostics.View.ReduceMotion);
    }
}
