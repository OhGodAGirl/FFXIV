using Exportaramus.Bootstrap;

namespace Exportaramus.Tests.Bootstrap;

public sealed class LifecycleShutdownTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ShutdownStartsEveryOwnerAndWaitsForItsLastWriteBeforeReleasingResources()
    {
        // Arrange: leave each shutdown task unfinished.
        var capture = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inventory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var diagnostics = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var configuration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var disposed = Task.Run(() => LifecycleCleanup.Run(
            () => LifecycleCleanup.Complete(
                () => { order.Enqueue("capture"); return new(capture.Task); },
                () => { order.Enqueue("inventory"); return new(inventory.Task); },
                () => { order.Enqueue("diagnostics"); return new(diagnostics.Task); },
                () => { order.Enqueue("configuration"); allStarted.SetResult(); return new(configuration.Task); }),
            () => order.Enqueue("resources")));
        try
        {
            // Act: finish capture, cache and report work; leave the settings write pending.
            await allStarted.Task.WaitAsync(Deadline);
            capture.SetResult();
            inventory.SetResult();
            diagnostics.SetResult();

            // Assert: wait for every task before releasing resources.
            Assert.False(disposed.IsCompleted);
            Assert.Equal(["capture", "inventory", "diagnostics", "configuration"], order);
            configuration.SetResult();
            await disposed.WaitAsync(Deadline);
            Assert.Equal(["capture", "inventory", "diagnostics", "configuration", "resources"], order);
        }
        finally
        {
            capture.TrySetResult();
            inventory.TrySetResult();
            diagnostics.TrySetResult();
            configuration.TrySetResult();
            await disposed.WaitAsync(Deadline);
        }
    }

    [Fact]
    public async Task ShutdownObservesFailuresAndStillWaitsForEveryIndependentOwner()
    {
        // Arrange: one shutdown throws, one task faults and one stays pending.
        var finalWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = false;
        var disposed = Task.Run(() => LifecycleCleanup.Run(
            () => LifecycleCleanup.Complete(
                () => throw new InvalidOperationException("startup"),
                () => new(Task.FromException(new IOException("worker"))),
                () => { started.SetResult(); return new(finalWrite.Task); }),
            () => released = true));
        try
        {
            // Act: finish the pending task after the other two fail.
            await started.Task.WaitAsync(Deadline);
            Assert.False(disposed.IsCompleted);
            Assert.False(released);
            finalWrite.SetResult();
            await disposed.WaitAsync(Deadline);

            // Assert: finish cleanup without rethrowing either error.
            Assert.True(released);
            LifecycleCleanup.Complete();
        }
        finally
        {
            finalWrite.TrySetResult();
            await disposed.WaitAsync(Deadline);
        }
    }

    [Fact]
    public void CompletedAndCancelledOwnersAreObservedOnTheCallingThread()
    {
        // Arrange: start cancellation before releasing host resources.
        var caller = Environment.CurrentManagedThreadId;
        var threads = new List<int>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act.
        LifecycleCleanup.Complete(
            () => { threads.Add(Environment.CurrentManagedThreadId); return ValueTask.CompletedTask; },
            () => { threads.Add(Environment.CurrentManagedThreadId); return new(Task.FromCanceled(cancellation.Token)); });

        // Assert.
        Assert.Equal([caller, caller], threads);
    }
}
