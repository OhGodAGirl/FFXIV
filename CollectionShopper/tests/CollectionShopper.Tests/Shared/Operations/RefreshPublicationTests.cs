namespace CollectionShopper.Tests;

public sealed class RefreshPublicationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionInvalidatedAfterDispatchIsQueuedCannotPublish(bool failedFetch)
    {
        // Arrange
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Action? queued = null;
        var current = true;
        var reports = 0;
        var publications = 0;
        var errors = 0;
        var operation = RefreshPublication.RunAsync(
            _ => failedFetch ? Task.FromException<int>(new InvalidOperationException("offline")) : Task.FromResult(42),
            (action, _) => { queued = action; return completion.Task; }, () => current,
            _ => publications++, _ => reports++, () => errors++, CancellationToken.None);
        Assert.NotNull(queued);
        Assert.False(operation.IsCompleted);

        // Act
        current = false;
        queued();
        completion.SetResult();
        await operation;

        // Assert
        Assert.Equal(failedFetch ? 1 : 0, reports);
        Assert.Equal(0, publications);
        Assert.Equal(0, errors);
    }

    [Fact]
    public async Task CancellationWhileDispatcherIsPendingIsQuietAndUsesTheOperationToken()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatchedToken = CancellationToken.None;
        var operation = RefreshPublication.RunAsync(_ => Task.FromResult(42), (_, token) =>
        {
            dispatchedToken = token;
            return completion.Task;
        }, () => true, _ => Assert.Fail("Cancelled result published"),
            _ => Assert.Fail("Cancellation reported as failure"),
            () => Assert.Fail("Cancellation published as failure"), cancellation.Token);

        // Act
        cancellation.Cancel();
        completion.SetCanceled(cancellation.Token);
        await operation;

        // Assert
        Assert.Equal(cancellation.Token, dispatchedToken);
        Assert.True(operation.IsCompletedSuccessfully);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SynchronousFetchDispatchAndPublicationFailuresReportOnce(int failingStage)
    {
        // Arrange
        var failure = new InvalidOperationException("refresh failed");
        var dispatches = 0;
        var reports = new List<Exception>();
        var errors = 0;
        var publications = 0;

        // Act
        await RefreshPublication.RunAsync(_ => failingStage == 0 ? throw failure : Task.FromResult(42),
            (action, _) =>
            {
                dispatches++;
                if (failingStage == 1 && dispatches == 1) throw failure;
                action();
                return Task.CompletedTask;
            }, () => true, _ => { publications++; throw failure; },
            reports.Add, () => errors++, CancellationToken.None);

        // Assert
        Assert.Same(failure, Assert.Single(reports));
        Assert.Equal(failingStage == 0 ? 1 : 2, dispatches);
        Assert.Equal(failingStage == 2 ? 1 : 0, publications);
        Assert.Equal(1, errors);
    }

    [Fact]
    public async Task ErrorDispatcherFailureEscapesWithoutRetryingOrReportingItAsFetchFailure()
    {
        // Arrange
        var fetchFailure = new InvalidOperationException("fetch failed");
        var dispatcherFailure = new InvalidOperationException("dispatcher unavailable");
        var reports = new List<Exception>();
        var dispatches = 0;

        // Act
        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() => RefreshPublication.RunAsync(
            _ => Task.FromException<int>(fetchFailure),
            (_, _) => { dispatches++; throw dispatcherFailure; }, () => true,
            _ => Assert.Fail("Unexpected result"), reports.Add,
            () => Assert.Fail("Unavailable dispatcher published an error"), CancellationToken.None));

        // Assert
        Assert.Same(dispatcherFailure, observed);
        Assert.Same(fetchFailure, Assert.Single(reports));
        Assert.Equal(1, dispatches);
    }

    private static readonly int[] ExpectedResult = [42];
    private static readonly string[] FailureOrder = ["report", "dispatch", "failure"];
    [Fact]
    public async Task CurrentResultIsPublishedOnTheDispatcherExactlyOnce()
    {
        // Arrange
        var published = new List<int>();
        var dispatched = false;
        // Act
        await RefreshPublication.RunAsync(_ => Task.FromResult(42), (action, _) =>
        {
            dispatched = true;
            action();
            return Task.CompletedTask;
        }, () => dispatched, published.Add, _ => Assert.Fail("Unexpected failure"),
            () => Assert.Fail("Unexpected error publication"), CancellationToken.None);
        // Assert
        Assert.Equal(ExpectedResult, published);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObsoleteResultOrFailureCannotMutateNewSession(bool fails)
    {
        // Arrange
        var fetch = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var current = true;
        var reports = new List<Exception>();
        var task = RefreshPublication.RunAsync(_ => fetch.Task, Dispatch, () => current,
            _ => Assert.Fail("Obsolete result published"), reports.Add,
            () => Assert.Fail("Obsolete error published"), CancellationToken.None);
        // Act
        current = false;
        if (fails) fetch.SetException(new InvalidOperationException("offline"));
        else fetch.SetResult(42);
        await task;
        // Assert
        Assert.Equal(fails ? 1 : 0, reports.Count);
    }

    [Fact]
    public async Task CancellationAfterFetchRejectsResultsWithoutReportingFailure()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var dispatched = false;
        // Act
        await RefreshPublication.RunAsync(_ =>
        {
            cancellation.Cancel();
            return Task.FromResult(42);
        }, (action, token) => { dispatched = true; return Dispatch(action, token); }, () => true,
            _ => Assert.Fail("Cancelled result published"), _ => Assert.Fail("Cancellation logged as failure"),
            () => Assert.Fail("Cancellation published as failure"), cancellation.Token);
        // Assert
        Assert.False(dispatched);
    }

    [Fact]
    public async Task FailureReportsOriginalExceptionAndPublishesErrorOnlyOnDispatcher()
    {
        // Arrange
        var expected = new InvalidOperationException("offline");
        var calls = new List<string>();
        // Act
        await RefreshPublication.RunAsync(_ => Task.FromException<int>(expected), (action, _) =>
        {
            calls.Add("dispatch");
            action();
            return Task.CompletedTask;
        }, () => true, _ => Assert.Fail("Failed result published"), ex =>
        {
            Assert.Same(expected, ex);
            calls.Add("report");
        }, () => calls.Add("failure"), CancellationToken.None);
        // Assert
        Assert.Equal(FailureOrder, calls);
    }

    [Fact]
    public async Task UnrelatedCancellationExceptionIsReportedAsFailure()
    {
        // Arrange
        var errors = 0;
        var reports = new List<Exception>();
        // Act
        await RefreshPublication.RunAsync(_ => Task.FromException<int>(new OperationCanceledException()),
            Dispatch, () => true, _ => Assert.Fail("Unexpected result"), reports.Add, () => errors++, CancellationToken.None);
        // Assert
        Assert.IsType<OperationCanceledException>(Assert.Single(reports));
        Assert.Equal(1, errors);
    }

    private static Task Dispatch(Action action, CancellationToken _)
    {
        action();
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task FetchAndBothDispatchPathsDoNotCaptureCallingContext(int pendingStage)
    {
        // Arrange: each asynchronous boundary starts on a caller-owned context.
        var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new ObservedContext();
        var previous = SynchronizationContext.Current;
        var published = 0;
        var failures = 0;
        Task operation;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            operation = RefreshPublication.RunAsync(
                _ => pendingStage == 0 ? pending.Task : pendingStage == 2
                    ? Task.FromException<int>(new InvalidOperationException("offline")) : Task.FromResult(42),
                (action, _) => { action(); return pendingStage == 0 ? Task.CompletedTask : pending.Task; },
                () => true, _ => published++, _ => { }, () => failures++, CancellationToken.None);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        // Act
        pending.SetResult(42);
        await operation.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert: publication uses the supplied dispatcher; continuations do not
        // schedule extra work on the caller's UI context.
        Assert.Equal(0, context.Posts);
        Assert.Equal(pendingStage == 2 ? 0 : 1, published);
        Assert.Equal(pendingStage == 2 ? 1 : 0, failures);
    }

    private sealed class ObservedContext : SynchronizationContext
    {
        private int posts;
        internal int Posts => Volatile.Read(ref posts);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref posts);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
    }
}
