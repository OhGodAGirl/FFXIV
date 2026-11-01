using System.Collections.Concurrent;
using Exportaramus.Features.Diagnostics;

namespace Exportaramus.Tests.Diagnostics;

public sealed class DiagnosticSourceContextTests
{
    [Fact]
    public async Task SourceCompletionDoesNotRequirePumpingTheSourceContext()
    {
        var sourceCompletion = new TaskCompletionSource<DiagnosticReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        var suspended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new RecordingContext();
        // ExecutionContext restoration happens after the worker registers its await.
        // This handshake avoids completing the source before the await can suspend.
        var marker = new AsyncLocal<bool>(change =>
        {
            if (change.ThreadContextChanged && change.PreviousValue && !change.CurrentValue)
                suspended.TrySetResult();
        });
        var controller = new DiagnosticsController(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            marker.Value = true;
            return sourceCompletion.Task;
        }, new DiagnosticMeasurements(), _ => { });
        controller.Dispatch(DiagnosticsIntent.GenerateReport);
        await suspended.Task;
        var completed = WaitForCompletion(controller);
        try
        {
            sourceCompletion.SetResult(new() { GeneratedAtUtc = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc) });
            Assert.Same(completed, await Task.WhenAny(completed, context.Posted.Task));
            await completed;
            controller.Update();
            Assert.NotNull(controller.View.Report);
        }
        finally
        {
            sourceCompletion.TrySetCanceled();
            context.Drain();
            await controller.DisposeAsync();
            await completed;
        }
    }

    private static async Task WaitForCompletion(DiagnosticsController controller)
    {
        while (controller.OwnedTask.State is "Waiting" or "Running")
            await Task.Delay(1);
    }

    private sealed class RecordingContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> callbacks = new();
        public TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            callbacks.Enqueue((callback, state));
            Posted.TrySetResult();
        }
        public void Drain()
        {
            while (callbacks.TryDequeue(out var item)) item.Callback(item.State);
        }
    }
}
