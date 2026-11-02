namespace CollectionShopper.Tests;

public sealed class LifecycleStressTests
{
    [Fact]
    public async Task RefreshActivityReleasesAfterSuccessfulOwnershipRefresh()
    {
        var activity = new RefreshActivity();

        Assert.True(activity.TryBegin());
        Assert.False(activity.TryBegin());
        Assert.True(activity.IsActive);
        await activity.RunAsync(_ => Task.CompletedTask, CancellationToken.None);

        Assert.False(activity.IsActive);
        Assert.True(activity.TryBegin());
        activity.AbortBegin();
    }

    [Fact]
    public async Task RefreshActivityRejectsNullOperationAndReleasesAfterAbort()
    {
        var activity = new RefreshActivity();

        Assert.True(activity.TryBegin());
        activity.AbortBegin();
        Assert.False(activity.IsActive);
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            activity.RunAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task RefreshActivityReleasesAfterFailure()
    {
        var activity = new RefreshActivity();
        Assert.True(activity.TryBegin());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            activity.RunAsync(
                _ => throw new InvalidOperationException("fixture failure"),
                CancellationToken.None));

        Assert.False(activity.IsActive);
    }

    [Fact]
    public async Task RefreshActivityReleasesAfterCancellation()
    {
        var activity = new RefreshActivity();
        using var cancellation = new CancellationTokenSource();
        Assert.True(activity.TryBegin());
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            activity.RunAsync(
                token => Task.Delay(Timeout.InfiniteTimeSpan, token),
                cancellation.Token));

        Assert.False(activity.IsActive);
    }

    [Fact]
    public async Task DuplicateStartIsRejectedAndNextGenerationCanRun()
    {
        using var operation = new SingleFlightOperation(CancellationToken.None);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(operation.TryStart(token => release.Task.WaitAsync(token)));
        Assert.False(operation.TryStart(_ => Task.CompletedTask));
        release.SetResult();
        await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(operation.TryStart(_ => Task.CompletedTask));
        await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(operation.LastException);
    }

    [Fact]
    public async Task DisposalDuringWorkCancelsWithoutLateMutation()
    {
        var operation = new SingleFlightOperation(CancellationToken.None);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mutatedAfterWait = false;
        Assert.True(operation.TryStart(async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            mutatedAfterWait = true;
        }));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        operation.Dispose();
        operation.Dispose();
        await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(mutatedAfterWait);
        Assert.False(operation.TryStart(_ => Task.CompletedTask));
        Assert.Null(operation.LastException);
    }

    [Fact]
    public async Task ExternalCancellationAndExplicitCancellationAreIdempotent()
    {
        using var lifetime = new CancellationTokenSource();
        using var operation = new SingleFlightOperation(lifetime.Token);
        operation.Cancel();
        Assert.True(operation.TryStart(token => Task.Delay(Timeout.InfiniteTimeSpan, token)));

        operation.Cancel();
        operation.Cancel();
        lifetime.Cancel();
        await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(operation.LastException);
    }

    [Fact]
    public void NullOperationIsRejected()
    {
        using var operation = new SingleFlightOperation(CancellationToken.None);

        Assert.Throws<ArgumentNullException>(() => operation.TryStart(null!));
        Assert.False(operation.IsRunning);
    }

    [Fact]
    public void DisposalBeforeInitializationIsSafeAndIdempotent()
    {
        var operation = new SingleFlightOperation(CancellationToken.None);

        operation.Dispose();
        operation.Cancel();
        operation.Dispose();

        Assert.False(operation.IsRunning);
        Assert.True(operation.Completion.IsCompletedSuccessfully);
        Assert.False(operation.TryStart(_ => Task.CompletedTask));
        Assert.Null(operation.LastException);
    }

    [Fact]
    public async Task CompletedGenerationCannotOverwriteOrCancelNextGeneration()
    {
        using var operation = new SingleFlightOperation(CancellationToken.None);
        Assert.True(operation.TryStart(_ => Task.CompletedTask));
        var firstCompletion = operation.Completion;
        await firstCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(operation.TryStart(async token =>
        {
            secondStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }));
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await firstCompletion;
        Assert.True(operation.IsRunning);
        operation.Cancel();
        await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(operation.IsRunning);
        Assert.Null(operation.LastException);
    }

    [Fact]
    public async Task FaultIsObservedAndDoesNotPoisonTheNextOperation()
    {
        using var operation = new SingleFlightOperation(CancellationToken.None);
        Assert.True(operation.TryStart(_ => throw new InvalidOperationException("fixture failure")));
        await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsType<InvalidOperationException>(operation.LastException);
        Assert.True(operation.TryStart(_ => Task.CompletedTask));
        await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(operation.LastException);
    }

    [Fact]
    public async Task RepeatedConstructionCancellationAndDisposalLeaveNoActiveWork()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var operation = new SingleFlightOperation(CancellationToken.None);
            Assert.True(operation.TryStart(token => Task.Delay(Timeout.InfiniteTimeSpan, token)));
            operation.Dispose();
            await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(operation.IsRunning);
            Assert.Null(operation.LastException);
        }
    }
}
