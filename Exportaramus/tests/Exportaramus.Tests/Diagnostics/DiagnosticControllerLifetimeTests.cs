using Exportaramus.Features.Diagnostics;
using Microsoft.Win32.SafeHandles;
using System.Collections.Concurrent;

namespace Exportaramus.Tests.Diagnostics;

public sealed class DiagnosticControllerLifetimeTests
{
    private static DiagnosticReport Report => new() { GeneratedAtUtc = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc) };

    [Fact]
    public async Task DisposalCanCompleteWithoutPumpingTheCallersSynchronizationContext()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<DiagnosticReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new DiagnosticsController(_ => { entered.SetResult(); return release.Task; }, new(), _ => { });
        var context = new QueuedContext();
        Task? disposing = null;
        try
        {
            controller.Dispatch(DiagnosticsIntent.GenerateReport); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var previous = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(context);
                disposing = controller.DisposeAsync().AsTask();
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }

            release.SetResult(Report);

            await disposing.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(controller.View.Running);
        }
        finally
        {
            release.TrySetResult(Report);
            context.Drain();
            if (disposing is not null) await disposing.WaitAsync(TimeSpan.FromSeconds(3));
            await controller.DisposeAsync();
        }
    }

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> pending = new();
        public override void Post(SendOrPostCallback callback, object? state) => pending.Enqueue((callback, state));
        public void Drain() { while (pending.TryDequeue(out var work)) work.Callback(work.State); }
    }

    [Fact]
    public async Task CancelledWorkerDoesNotSerializeLateInvalidMetadataOrInventAReportFailure()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<DiagnosticReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        var measurements = new DiagnosticMeasurements();
        await using var controller = new DiagnosticsController(_ => { entered.SetResult(); return release.Task; }, measurements, _ => { });
        try
        {
            controller.Dispatch(DiagnosticsIntent.GenerateReport); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            controller.Dispatch(DiagnosticsIntent.Close);

            release.SetResult(Report with { ReportStatus = "Invalid" });
            await DiagnosticWorkerTest.DrainAsync(controller);

            Assert.Equal("NotStarted", controller.OwnedTask.State); Assert.Null(controller.View.Report); Assert.Empty(measurements.Failures);
            Assert.Equal("Ready", controller.View.Status);
        }
        finally { release.TrySetResult(Report); }
    }

    [Fact]
    public async Task CompletedReportReleasesItsCancellationWaitHandle()
    {
        SafeWaitHandle? handle = null;
        await using var controller = new DiagnosticsController(token =>
        {
            handle = token.WaitHandle.SafeWaitHandle;
            return Task.FromResult(Report);
        }, new(), _ => { });

        controller.Dispatch(DiagnosticsIntent.GenerateReport);
        await DiagnosticWorkerTest.DrainAsync(controller);

        Assert.NotNull(controller.View.Report); Assert.NotNull(handle); Assert.True(handle.IsClosed);
    }

    [Fact]
    public async Task DisposalWaitsForTheOwnedWorkerAndThenReleasesItsWaitHandle()
    {
        var started = new TaskCompletionSource<SafeWaitHandle>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new DiagnosticsController(async token =>
        {
            started.SetResult(token.WaitHandle.SafeWaitHandle);
            await release.Task;
            return Report;
        }, new(), _ => { });
        Task? disposing = null;
        try
        {
            controller.Dispatch(DiagnosticsIntent.GenerateReport);
            var handle = await started.Task.WaitAsync(TimeSpan.FromSeconds(3));

            disposing = controller.DisposeAsync().AsTask();

            Assert.False(disposing.IsCompleted); Assert.False(handle.IsClosed);
            release.SetResult(); await disposing.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(handle.IsClosed); Assert.False(controller.View.Running); Assert.Null(controller.View.Report);
        }
        finally
        {
            release.TrySetResult();
            if (disposing is not null) await disposing.WaitAsync(TimeSpan.FromSeconds(3));
            await controller.DisposeAsync();
        }
    }
}
