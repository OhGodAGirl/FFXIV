namespace BetterGlamorPlates.Shared.Operations;

/// <summary>
/// Owns scheduled tasks, cancellation, fault observation, and deferred cleanup
/// without synchronously waiting during disposal.
/// </summary>
internal sealed class OwnedOperationRegistry : IDisposable
{
    private readonly object gate = new();
    private readonly List<Action> releasesWhenIdle = [];
    private readonly CancellationTokenSource shutdown = new();
    private readonly Action<Exception> reportError;
    private TaskCompletionSource idle = CreateCompletedSignal();
    private int activeCount;
    private bool disposed;

    internal OwnedOperationRegistry(Action<Exception> reportError)
    {
        ArgumentNullException.ThrowIfNull(reportError);
        this.reportError = reportError;
    }

    internal int ActiveCount
    {
        get
        {
            lock (gate)
                return activeCount;
        }
    }

    internal Task WhenIdle => idle.Task;

    internal void AddDisposeWhenIdle(Action release)
    {
        ArgumentNullException.ThrowIfNull(release);
        Action? invokeNow = null;
        lock (gate)
        {
            if (disposed && activeCount == 0)
            {
                invokeNow = release;
            }
            else
            {
                releasesWhenIdle.Add(release);
            }
        }

        if (invokeNow is not null)
            InvokeRelease(invokeNow);
    }

    internal bool TryStart(Func<CancellationToken, Task> start)
    {
        ArgumentNullException.ThrowIfNull(start);
        CancellationToken token;
        lock (gate)
        {
            if (disposed)
                return false;

            if (activeCount == 0)
            {
                idle = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            activeCount++;
            token = shutdown.Token;
        }

        Task task;
        Exception? failure = null;
        try
        {
            task = start(token) ?? throw new InvalidOperationException(
                "Owned operation returned no task.");
        }
        catch (Exception exception)
        {
            failure = exception;
            task = Task.CompletedTask;
        }

        if (failure is not null)
        {
            reportError(failure);
            CompleteFailedStart();
            return false;
        }

        task.ContinueWith(
            static (completed, state) => ((OwnedOperationRegistry)state!).Complete(completed),
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return true;
    }

    public void Dispose()
    {
        Action[] releases;
        lock (gate)
        {
            if (disposed)
                return;

            disposed = true;
            shutdown.Cancel();
            releases = CompleteDisposalIfIdle();
        }

        InvokeReleases(releases);
    }

    private void Complete(Task task)
    {
        if (task.IsFaulted && task.Exception is { } aggregate)
        {
            foreach (var exception in aggregate.Flatten().InnerExceptions)
                reportError(exception);
        }

        Action[] releases;
        lock (gate)
        {
            activeCount--;
            if (activeCount == 0)
                idle.TrySetResult();
            releases = CompleteDisposalIfIdle();
        }

        InvokeReleases(releases);
    }

    private void CompleteFailedStart()
    {
        Action[] releases;
        lock (gate)
        {
            activeCount--;
            if (activeCount == 0)
                idle.TrySetResult();
            releases = CompleteDisposalIfIdle();
        }

        InvokeReleases(releases);
    }

    private Action[] CompleteDisposalIfIdle()
    {
        if (!disposed || activeCount != 0)
            return [];

        shutdown.Dispose();
        var releases = releasesWhenIdle.ToArray();
        releasesWhenIdle.Clear();
        return releases;
    }

    private static TaskCompletionSource CreateCompletedSignal()
    {
        var signal = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult();
        return signal;
    }

    private void InvokeReleases(Action[] releases)
    {
        foreach (var release in releases)
            InvokeRelease(release);
    }

    private void InvokeRelease(Action release)
    {
        try
        {
            release();
        }
        catch (Exception exception)
        {
            reportError(exception);
        }
    }
}
