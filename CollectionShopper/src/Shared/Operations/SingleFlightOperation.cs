namespace CollectionShopper;

/// <summary>
/// Owns one asynchronous operation at a time and observes every terminal state.
/// </summary>
internal sealed class SingleFlightOperation : IDisposable
{
    private readonly object gate = new();
    private readonly CancellationToken lifetimeToken;
    private CancellationTokenSource? activeCancellation;
    private Task completion = Task.CompletedTask;
    private Exception? lastException;
    private bool disposed;

    internal SingleFlightOperation(CancellationToken lifetimeToken)
    {
        this.lifetimeToken = lifetimeToken;
    }

    internal bool IsRunning
    {
        get
        {
            lock (gate)
                return !completion.IsCompleted;
        }
    }

    internal Task Completion
    {
        get
        {
            lock (gate)
                return completion;
        }
    }

    internal Exception? LastException
    {
        get
        {
            lock (gate)
                return lastException;
        }
    }

    internal bool TryStart(Func<CancellationToken, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        lock (gate)
        {
            if (disposed || !completion.IsCompleted)
                return false;

            lastException = null;
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
            var startGate = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            activeCancellation = cancellation;
            completion = ObserveAsync(startGate.Task, operation, cancellation);
            startGate.SetResult();
            return true;
        }
    }

    internal void Cancel()
    {
        lock (gate)
            activeCancellation?.Cancel();
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            activeCancellation?.Cancel();
        }
    }

    private async Task ObserveAsync(
        Task startGate,
        Func<CancellationToken, Task> operation,
        CancellationTokenSource cancellation)
    {
        await startGate.ConfigureAwait(false);
        try
        {
            await operation(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            lock (gate)
                lastException = ex;
        }
        finally
        {
            lock (gate)
                activeCancellation = null;

            cancellation.Dispose();
        }
    }

}
