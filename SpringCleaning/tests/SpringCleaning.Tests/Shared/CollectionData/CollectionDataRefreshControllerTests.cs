using SpringCleaning.Shared.CollectionData;

namespace SpringCleaning.Tests.CollectionData;

public sealed class CollectionDataRefreshControllerTests
{
    [Fact]
    public void RoutinePollKeepsSnapshotButNoticeAndCharacterChangeRevokeIt()
    {
        using var controller = new CollectionDataRefreshController();
        var snapshot = CreateSnapshot(42, 7);
        controller.Observe(42, false);
        controller.Complete(42, CollectionDataParseResult.Accept(snapshot), snapshot);

        controller.Observe(42, true, invalidated: false);
        Assert.Same(snapshot, controller.LatestSnapshot);
        controller.Observe(42, true, invalidated: true);
        Assert.Null(controller.LatestSnapshot);
        controller.Complete(42, CollectionDataParseResult.Accept(snapshot), snapshot);
        Assert.Null(controller.LatestSnapshot);
        controller.Complete(42, CollectionDataParseResult.Accept(snapshot), snapshot);
        Assert.Same(snapshot, controller.LatestSnapshot);
        controller.Observe(43, false, invalidated: false);
        Assert.Null(controller.LatestSnapshot);
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void InitialStatusIsIdleWithoutACharacter()
    {
        using var controller = new CollectionDataRefreshController();
        Assert.Equal(CollectionDataFailure.NoCharacter, controller.Snapshot.LastFailure);
        Assert.False(controller.Snapshot.RefreshRequested);
        Assert.False(controller.Snapshot.RefreshInProgress);

        var noCharacter = controller.Observe(0, providerRequested: true);
        Assert.False(noCharacter.ClearTransport);
        Assert.Null(noCharacter.RefreshRequest);
        Assert.False(controller.Snapshot.RefreshRequested);
    }

    [Fact]
    public void FirstObservationRequestsARefreshForTheActiveCharacter()
    {
        var controller = new CollectionDataRefreshController();

        var observation = controller.Observe(42, providerRequested: false);

        Assert.False(observation.ClearTransport);
        Assert.Equal(new CollectionDataRefreshRequest(42), observation.RefreshRequest);
        Assert.True(controller.Snapshot.RefreshInProgress);
        Assert.False(controller.Snapshot.HasSnapshot);
        Assert.Null(controller.LatestSnapshot);
    }

    [Fact]
    public void ProviderRequestsDuringRefreshCoalesceIntoOneFollowUpRefresh()
    {
        var controller = new CollectionDataRefreshController();
        var first = controller.Observe(42, providerRequested: false);

        var queued = controller.Observe(42, providerRequested: true);
        var followUp = controller.Complete(
            first.RefreshRequest!.Value.ContentId,
            CollectionDataParseResult.Accept(CreateSnapshot(42, 7)),
            CreateSnapshot(42, 7));

        Assert.Null(queued.RefreshRequest);
        Assert.Equal(new CollectionDataRefreshRequest(42), followUp);
        Assert.False(controller.Snapshot.HasSnapshot);
        Assert.Null(controller.LatestSnapshot);
        Assert.True(controller.Snapshot.RefreshInProgress);
    }

    [Fact]
    public void CharacterChangeClearsPreviousSnapshotAndIgnoresStaleCompletion()
    {
        var controller = new CollectionDataRefreshController();
        var first = controller.Observe(42, providerRequested: false);
        _ = controller.Complete(
            first.RefreshRequest!.Value.ContentId,
            CollectionDataParseResult.Accept(CreateSnapshot(42, 7)),
            CreateSnapshot(42, 7));

        var changed = controller.Observe(77, providerRequested: false);
        var staleFollowUp = controller.Complete(
            refreshedContentId: 42,
            CollectionDataParseResult.Accept(CreateSnapshot(42, 8)),
            CreateSnapshot(42, 8));

        Assert.True(changed.ClearTransport);
        Assert.Equal(new CollectionDataRefreshRequest(77), changed.RefreshRequest);
        Assert.Null(staleFollowUp);
        Assert.False(controller.Snapshot.HasSnapshot);
        Assert.True(controller.Snapshot.RefreshInProgress);
        Assert.Equal(CollectionDataFailure.None, controller.Snapshot.LastFailure);
    }

    [Fact]
    public void CharacterChangeBeforeFirstCompletionStillClearsTransport()
    {
        using var controller = new CollectionDataRefreshController();
        var first = controller.Observe(42, providerRequested: false);

        var changed = controller.Observe(77, providerRequested: false);
        Assert.True(changed.ClearTransport);
        Assert.Null(changed.RefreshRequest);

        Assert.Equal(
            new CollectionDataRefreshRequest(77),
            controller.Complete(
                first.RefreshRequest!.Value.ContentId,
                CollectionDataParseResult.Accept(CreateSnapshot(42, 1)),
                null));
        Assert.False(controller.Snapshot.HasSnapshot);
        Assert.Equal(CollectionDataFailure.None, controller.Snapshot.LastFailure);
    }

    [Fact]
    public void LogoutClearsSnapshotAndStopsFurtherRefreshes()
    {
        var controller = new CollectionDataRefreshController();
        var first = controller.Observe(42, providerRequested: false);
        _ = controller.Complete(
            first.RefreshRequest!.Value.ContentId,
            CollectionDataParseResult.Accept(CreateSnapshot(42, 7)),
            CreateSnapshot(42, 7));

        var logout = controller.Observe(0, providerRequested: true);
        var staleFollowUp = controller.Complete(
            refreshedContentId: 42,
            CollectionDataParseResult.Accept(CreateSnapshot(42, 8)),
            CreateSnapshot(42, 8));

        Assert.True(logout.ClearTransport);
        Assert.Null(logout.RefreshRequest);
        Assert.Null(staleFollowUp);
        Assert.False(controller.Snapshot.HasSnapshot);
        Assert.False(controller.Snapshot.RefreshInProgress);
        Assert.Equal(CollectionDataFailure.NoCharacter, controller.Snapshot.LastFailure);
    }

    [Fact]
    public void IdleProviderNoticeStartsExactlyOneRefresh()
    {
        using var controller = new CollectionDataRefreshController();
        var first = controller.Observe(42, providerRequested: false);
        Assert.Null(controller.Complete(
            first.RefreshRequest!.Value.ContentId,
            CollectionDataParseResult.Accept(CreateSnapshot(42, 7)),
            null));

        var idle = controller.Observe(42, providerRequested: false);
        var requested = controller.Observe(42, providerRequested: true);

        Assert.Null(idle.RefreshRequest);
        Assert.Equal(new CollectionDataRefreshRequest(42), requested.RefreshRequest);
        Assert.True(controller.Snapshot.RefreshInProgress);
        Assert.False(controller.Snapshot.RefreshRequested);
    }

    [Fact]
    public void RejectedRefreshInvalidatesEvenMatchingSameCharacterFallback()
    {
        using var controller = new CollectionDataRefreshController();
        var first = controller.Observe(42, providerRequested: false);
        Assert.Null(controller.Complete(
            first.RefreshRequest!.Value.ContentId,
            CollectionDataParseResult.Reject(CollectionDataFailure.MalformedJson),
            CreateSnapshot(42, 6)));
        Assert.False(controller.Snapshot.HasSnapshot);
        Assert.Null(controller.Snapshot.Revision);
        Assert.Equal(CollectionDataFailure.MalformedJson, controller.Snapshot.LastFailure);

        var second = controller.Observe(77, providerRequested: false);
        Assert.Null(controller.Complete(
            second.RefreshRequest!.Value.ContentId,
            CollectionDataParseResult.Reject(CollectionDataFailure.ProviderUnavailable),
            CreateSnapshot(42, 9)));
        Assert.False(controller.Snapshot.HasSnapshot);
        Assert.Equal(CollectionDataFailure.ProviderUnavailable, controller.Snapshot.LastFailure);
    }

    [Fact]
    public void RejectedRefreshWithoutTransportFallbackKeepsSnapshotUnavailable()
    {
        using var controller = new CollectionDataRefreshController();
        var first = controller.Observe(42, providerRequested: false);

        Assert.Null(controller.Complete(
            first.RefreshRequest!.Value.ContentId,
            CollectionDataParseResult.Reject(CollectionDataFailure.ProviderUnavailable),
            latestFromTransport: null));
        Assert.Null(controller.LatestSnapshot);
        Assert.False(controller.Snapshot.HasSnapshot);
        Assert.Equal(CollectionDataFailure.ProviderUnavailable, controller.Snapshot.LastFailure);
    }

    [Fact]
    public void CompletionRequiresTheActiveInFlightIdentity()
    {
        using var controller = new CollectionDataRefreshController();
        Assert.Null(controller.Complete(42, CollectionDataParseResult.Accept(CreateSnapshot(42, 1)), null));
        Assert.Null(controller.Complete(0, CollectionDataParseResult.Accept(CreateSnapshot(42, 1)), null));

        var first = controller.Observe(42, providerRequested: false);
        Assert.Null(controller.Complete(77, CollectionDataParseResult.Accept(CreateSnapshot(77, 1)), null));
        Assert.True(controller.Snapshot.RefreshInProgress);

        Assert.Null(controller.Complete(first.RefreshRequest!.Value.ContentId, CollectionDataParseResult.Accept(CreateSnapshot(42, 1)), null));
        Assert.False(controller.Snapshot.RefreshInProgress);
    }

    [Fact]
    public void DisposalIsTerminalAndPublishesDisposedState()
    {
        var controller = new CollectionDataRefreshController();
        _ = controller.Observe(42, providerRequested: false);
        controller.Dispose();
        controller.Dispose();

        Assert.Equal(CollectionDataFailure.Disposed, controller.Snapshot.LastFailure);
        Assert.False(controller.Snapshot.HasSnapshot);
        Assert.False(controller.Snapshot.RefreshRequested);
        Assert.False(controller.Snapshot.RefreshInProgress);
        Assert.Equal(default, controller.Observe(42, providerRequested: true));
        Assert.Null(controller.Complete(42, CollectionDataParseResult.Accept(CreateSnapshot(42, 2)), null));
    }

    private static CollectionDataSnapshot CreateSnapshot(ulong contentId, ulong revision) => new(
        "CollectorsCompendium",
        "0.3.0.0",
        contentId,
        revision,
        Now,
        Source(CollectionSourceStatus.Fresh),
        Source(CollectionSourceStatus.Fresh),
        Source(CollectionSourceStatus.Fresh),
        Source(CollectionSourceStatus.Fresh),
        Source(CollectionSourceStatus.Fresh),
        Source(CollectionSourceStatus.Fresh),
        Source(CollectionSourceStatus.NeverObserved));

    private static CollectionSourceSnapshot Source(CollectionSourceStatus status) => status switch
    {
        CollectionSourceStatus.NeverObserved => new(status, null, [], null),
        _ => new(status, Now, [1], null),
    };
}
