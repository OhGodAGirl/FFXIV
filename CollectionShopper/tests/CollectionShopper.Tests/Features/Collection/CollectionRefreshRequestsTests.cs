namespace CollectionShopper.Tests;

public sealed class CollectionRefreshRequestsTests
{
    // A broken/mutated owner must fail instead of waiting forever for a signal.
    // This deadline does not sequence the fixture or establish successful behavior.
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Fact]
    public void RequestsAreNotConsumedUntilBothActivityAndOperationHaveSettled()
    {
        using var requests = new CollectionRefreshRequests();
        requests.RequestCatalog(false);
        requests.RequestPrices();
        Assert.False(requests.TryTakeCatalog(false, true));
        Assert.False(requests.TryTakePrices(true));
        Assert.True(requests.TryTakeCatalog(false, false));
        Assert.False(requests.TryTakeCatalog(false, false));
        Assert.False(requests.TryTakePrices(false));
    }

    [Fact]
    public void RepeatedEnsureLoadedCoalescesOnlyTheCurrentCharacterCatalog()
    {
        using var requests = new CollectionRefreshRequests();
        requests.RequestCatalog(false);
        Assert.True(requests.TryTakeCatalog(false, false));
        requests.RequestCatalog(true);
        Assert.False(requests.TryTakeCatalog(false, false));
        requests.Invalidate(false);
        requests.RequestCatalog(true);
        Assert.False(requests.TryTakeCatalog(false, true));
        Assert.True(requests.TryTakeCatalog(false, false));
        requests.RequestCatalog(false);
        Assert.True(requests.TryTakeCatalog(true, false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MarketRequestLoadsCatalogWhenNeededAndOtherwiseOnlyPrices(bool loaded)
    {
        using var requests = new CollectionRefreshRequests();
        Assert.False(requests.TryTakePrices(false));
        requests.RequestPrices();
        Assert.Equal(!loaded, requests.TryTakeCatalog(loaded, false));
        Assert.Equal(loaded, requests.TryTakePrices(false));
        Assert.False(requests.TryTakeCatalog(false, false));
        Assert.False(requests.TryTakePrices(false));
    }

    [Fact]
    public void CatalogRequestDuringPriceRefreshRemainsPending()
    {
        using var requests = new CollectionRefreshRequests();
        requests.RequestPrices();
        Assert.True(requests.TryTakePrices(false));
        requests.RequestCatalog(true);
        Assert.False(requests.TryTakeCatalog(true, true));
        Assert.True(requests.TryTakeCatalog(true, false));
    }

    [Fact]
    public void LogoutClearsPriorPriceRequestsAndAutomaticLoginQueuesOneCatalog()
    {
        using var requests = new CollectionRefreshRequests();
        requests.RequestPrices();
        requests.Invalidate(false);
        Assert.False(requests.TryTakeCatalog(false, false));
        Assert.False(requests.TryTakePrices(false));
        requests.Invalidate(true);
        Assert.True(requests.TryTakeCatalog(false, false));
        Assert.False(requests.TryTakeCatalog(false, false));
    }

    [Fact]
    public void DisposalClearsPendingRequestsAndRejectsFurtherWorkIdempotently()
    {
        var requests = new CollectionRefreshRequests();
        requests.RequestCatalog(false);
        requests.RequestPrices();
        requests.Dispose();
        requests.Dispose();
        requests.RequestCatalog(false);
        requests.RequestPrices();
        requests.Invalidate(true);
        Assert.False(requests.TryTakeCatalog(false, false));
        Assert.False(requests.TryTakePrices(false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedOldCharacterCompletionCannotPublishOrConsumeTheNewCharacterRequest(bool relogSameCharacter)
    {
        using var requests = new CollectionRefreshRequests();
        using var session = new CollectionDataOwnershipSession();
        using var operation = new SingleFlightOperation(CancellationToken.None);
        var activity = new RefreshActivity();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldVersion = session.Begin(ExportaramusFixture.ContentId);
        requests.RequestCatalog(false);
        Assert.True(requests.TryTakeCatalog(false, false));
        var published = false;
        Assert.True(activity.TryBegin());
        Assert.True(operation.TryStart(token => activity.RunAsync(async cancellation =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(cancellation);
            published = session.TryPublish(ExportaramusFixture.ContentId, oldVersion, ExportaramusFixture.State());
        }, token)));
        await entered.Task.WaitAsync(Deadline);

        session.Invalidate();
        requests.Invalidate(false);
        var nextId = relogSameCharacter ? ExportaramusFixture.ContentId : 42UL;
        var nextVersion = session.Begin(nextId);
        requests.Invalidate(true);
        requests.RequestCatalog(activity.IsActive || operation.IsRunning);
        Assert.False(requests.TryTakeCatalog(false, activity.IsActive || operation.IsRunning));
        release.SetResult();
        await operation.Completion.WaitAsync(Deadline);
        Assert.False(published);
        Assert.Null(session.State.Snapshot);
        Assert.True(session.IsCurrent(nextId, nextVersion));
        Assert.True(requests.TryTakeCatalog(false, activity.IsActive || operation.IsRunning));
        Assert.False(requests.TryTakeCatalog(false, false));
    }

    [Fact]
    public async Task DisposalDuringDelayedWorkBlocksLatePublicationAndQueuedReload()
    {
        using var requests = new CollectionRefreshRequests();
        using var session = new CollectionDataOwnershipSession();
        using var operation = new SingleFlightOperation(CancellationToken.None);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var version = session.Begin(ExportaramusFixture.ContentId);
        var published = false;
        requests.RequestCatalog(false);
        Assert.True(operation.TryStart(async _ =>
        {
            entered.SetResult();
            await release.Task;
            published = session.TryPublish(ExportaramusFixture.ContentId, version, ExportaramusFixture.State());
        }));
        await entered.Task.WaitAsync(Deadline);
        requests.Dispose();
        session.Dispose();
        operation.Dispose();
        release.SetResult();
        await operation.Completion.WaitAsync(Deadline);
        Assert.False(published);
        Assert.False(requests.TryTakeCatalog(false, false));
        Assert.Null(session.State.Snapshot);
    }
}
