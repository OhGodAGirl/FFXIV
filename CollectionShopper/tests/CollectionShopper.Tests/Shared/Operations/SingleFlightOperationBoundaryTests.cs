namespace CollectionShopper.Tests;

public sealed class SingleFlightOperationBoundaryTests
{
    // Deadlines only fail a broken/mutated implementation that never signals its
    // controlled completion. They do not order operations or establish success.
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task StartNeverInvokesTheDelegateInlineOnTheDispatchStack()
    {
        using var dispatchStack = new ThreadLocal<bool>();
        using var operation = new SingleFlightOperation(CancellationToken.None);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var previousContext = SynchronizationContext.Current;
        try
        {
            // A thread-local marker distinguishes inline execution from work that
            // later uses the same pool thread; thread IDs alone cannot do that.
            SynchronizationContext.SetSynchronizationContext(null);
            dispatchStack.Value = true;
            Assert.True(operation.TryStart(_ =>
            {
                entered.SetResult(dispatchStack.Value);
                return Task.CompletedTask;
            }));
        }
        finally
        {
            dispatchStack.Value = false;
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        Assert.False(await entered.Task.WaitAsync(Deadline));
        await operation.Completion.WaitAsync(Deadline);
        Assert.Null(operation.LastException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingContinuationsDoNotDependOnCallerOrDelegateContext(bool delegateInstallsContext)
    {
        using var operation = new SingleFlightOperation(CancellationToken.None);
        var context = new RecordingContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var previousContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            Assert.True(operation.TryStart(_ =>
            {
                // The delegate can hand back pending work after installing its own
                // dispatcher context. The owner must not inherit that dependency.
                if (delegateInstallsContext)
                    SynchronizationContext.SetSynchronizationContext(context);
                entered.SetResult();
                return release.Task;
            }));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        try
        {
            await entered.Task.WaitAsync(Deadline);
        }
        finally
        {
            release.TrySetResult();
        }
        await operation.Completion.WaitAsync(Deadline);

        Assert.Equal(0, context.Posts);
        Assert.Null(operation.LastException);
    }

    [Fact]
    public async Task UnrelatedCancellationIsObservedAsAFailure()
    {
        using var operation = new SingleFlightOperation(CancellationToken.None);
        var exception = new OperationCanceledException(new CancellationToken(true));
        Assert.True(operation.TryStart(_ => Task.FromException(exception)));

        await operation.Completion.WaitAsync(Deadline);

        Assert.Same(exception, operation.LastException);
        Assert.True(operation.Completion.IsCompletedSuccessfully);
        Assert.False(operation.IsRunning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LifetimeCancellationAloneCancelsActiveOrNotYetStartedWork(bool cancelledBeforeStart)
    {
        using var lifetime = new CancellationTokenSource();
        if (cancelledBeforeStart)
            lifetime.Cancel();
        using var operation = new SingleFlightOperation(lifetime.Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = false;
        Assert.True(operation.TryStart(async token =>
        {
            entered.SetResult();
            try
            {
                await pending.Task.WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                cancellationObserved = true;
                throw;
            }
        }));

        await entered.Task.WaitAsync(Deadline);
        lifetime.Cancel();
        try
        {
            await operation.Completion.WaitAsync(Deadline);
        }
        finally
        {
            pending.TrySetResult();
        }

        Assert.True(cancellationObserved);
        Assert.Null(operation.LastException);
        Assert.False(operation.IsRunning);
    }

    [Fact]
    public async Task CompletedGenerationDoesNotReceiveTheNextLifetimeCancellation()
    {
        using var lifetime = new CancellationTokenSource();
        using var operation = new SingleFlightOperation(lifetime.Token);
        var completedCallbacks = 0;
        var activeCallbacks = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration completedRegistration = default;
        CancellationTokenRegistration activeRegistration = default;
        try
        {
            Assert.True(operation.TryStart(token =>
            {
                completedRegistration = token.Register(() => Interlocked.Increment(ref completedCallbacks));
                return Task.CompletedTask;
            }));
            await operation.Completion.WaitAsync(Deadline);

            Assert.True(operation.TryStart(async token =>
            {
                activeRegistration = token.Register(() => Interlocked.Increment(ref activeCallbacks));
                entered.SetResult();
                await pending.Task.WaitAsync(token).ConfigureAwait(false);
            }));
            await entered.Task.WaitAsync(Deadline);
            lifetime.Cancel();
            await operation.Completion.WaitAsync(Deadline);

            Assert.Equal(0, Volatile.Read(ref completedCallbacks));
            Assert.Equal(1, Volatile.Read(ref activeCallbacks));
            Assert.Null(operation.LastException);
        }
        finally
        {
            pending.TrySetResult();
            completedRegistration.Dispose();
            activeRegistration.Dispose();
        }
    }

    private sealed class RecordingContext : SynchronizationContext
    {
        private int posts;

        internal int Posts => Volatile.Read(ref posts);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref posts);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var previous = Current;
                try
                {
                    SetSynchronizationContext(this);
                    callback(state);
                }
                finally
                {
                    SetSynchronizationContext(previous);
                }
            });
        }
    }
}
