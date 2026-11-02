using BetterGlamorPlates.Bootstrap;
using BetterGlamorPlates.Shared.CollectionData;
using BetterGlamorPlates.Shared.Operations;

namespace BetterGlamorPlates.Tests.Bootstrap;

/// <summary>Verifies collection coordinator tests.</summary>
public sealed class CollectionCoordinatorTests
{
    /// <summary>The first login after no character clears stale facts without reporting a character switch.</summary>
    [Fact]
    public void InitialAbsenceAndFirstLoginHaveDistinctDiagnostics()
    {
        using var fixture = new Fixture { Owner = 0 };
        fixture.Coordinator.SynchronizeCharacter();
        Assert.Equal("Shared collection data cleared because no character is active.", Assert.Single(fixture.Messages));
        fixture.Owner = 42;
        fixture.Coordinator.SynchronizeCharacter();
        Assert.Single(fixture.Messages);
        Assert.Equal(6, fixture.Cleared.Count);
    }

    /// <summary>Malformed accepted results fail with the documented missing-data argument rather than ambiguous null dereferences.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedAcceptedResultRetainsTypedFailure(bool missingSource)
    {
        using var fixture = new Fixture();
        fixture.Coordinator.SynchronizeCharacter();
        fixture.Coordinator.TryQueueRefresh();
        var snapshot = missingSource ? Snapshot() with { Inventory = null! } : null;
        await fixture.Complete(new(snapshot, CollectionDataFailure.None));
        var error = Assert.IsType<ArgumentNullException>(Assert.Single(fixture.Errors));
        Assert.Equal(missingSource ? "source" : "snapshot", error.ParamName);
        Assert.Empty(fixture.Messages);
        Assert.Single(fixture.Requested);
    }
    /// <summary>Verifies character transitions clear both sources and preview only once per change.</summary>
    [Fact]
    public void CharacterTransitionsClearBothSourcesAndPreviewOnlyOncePerChange()
    {
        using var fixture = new Fixture();
        fixture.Coordinator.SynchronizeCharacter();
        fixture.Coordinator.SynchronizeCharacter();
        Assert.Equal(["preview", "client", "controller"], fixture.Cleared);
        Assert.Empty(fixture.Messages);

        fixture.Owner = 99;
        fixture.Coordinator.SynchronizeCharacter();
        fixture.Owner = 0;
        fixture.Coordinator.SynchronizeCharacter();
        fixture.Coordinator.SynchronizeCharacter();

        Assert.Equal(9, fixture.Cleared.Count);
        Assert.Equal(["Shared collection data cleared for a character change.",
            "Shared collection data cleared because no character is active."], fixture.Messages);
    }

    /// <summary>Verifies snapshot publication tracks reference changes including withdrawal.</summary>
    [Fact]
    public void SnapshotPublicationTracksReferenceChangesIncludingWithdrawal()
    {
        using var fixture = new Fixture();
        fixture.Coordinator.ObserveSnapshot();
        Assert.Empty(fixture.Published);
        fixture.Latest = Snapshot();
        fixture.Coordinator.ObserveSnapshot();
        fixture.Coordinator.ObserveSnapshot();
        fixture.Latest = null;
        fixture.Coordinator.ObserveSnapshot();
        Assert.Equal(2, fixture.Published.Count);
        Assert.True(fixture.Published[0].Accepted);
        Assert.Equal(CollectionDataFailure.ProviderUnavailable, fixture.Published[1].Failure);
    }

    /// <summary>Verifies duplicate refresh signals do not consume or start another request.</summary>
    [Fact]
    public async Task DuplicateRefreshSignalsDoNotConsumeOrStartAnotherRequest()
    {
        using var fixture = new Fixture();
        fixture.Coordinator.TryQueueRefresh();
        Assert.Equal(0, fixture.Taken);
        fixture.Coordinator.SynchronizeCharacter();
        fixture.Coordinator.TryQueueRefresh();
        fixture.Coordinator.TryQueueRefresh();
        Assert.Equal(1, fixture.Taken);
        Assert.Equal([42ul], fixture.Requested);

        await fixture.Complete(CollectionDataParseResult.Accept(Snapshot()));
        fixture.Coordinator.TryQueueRefresh();
        Assert.Equal(2, fixture.Taken);
        await fixture.Complete(CollectionDataParseResult.Accept(Snapshot()));
        Assert.Equal(2, fixture.Published.Count);
        Assert.Single(fixture.Messages);
    }

    /// <summary>Verifies missing owner and no pending request do not start work.</summary>
    [Fact]
    public void MissingOwnerAndNoPendingRequestDoNotStartWork()
    {
        using var fixture = new Fixture { Owner = 0 };
        fixture.Coordinator.SynchronizeCharacter();
        fixture.Coordinator.TryQueueRefresh();
        Assert.Empty(fixture.Requested);
        fixture.Owner = 42;
        fixture.Pending = false;
        fixture.Coordinator.SynchronizeCharacter();
        fixture.Coordinator.TryQueueRefresh();
        Assert.Equal(1, fixture.Taken);
        Assert.Empty(fixture.Requested);
    }

    /// <summary>Verifies publication routes failures without inventing ownership.</summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(10, false)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task PublicationRoutesFailuresWithoutInventingOwnership(int failureValue, bool retained)
    {
        using var fixture = new Fixture();
        fixture.Coordinator.SynchronizeCharacter();
        fixture.Cleared.Clear();
        if (retained) fixture.Latest = Snapshot();
        fixture.Coordinator.TryQueueRefresh();

        var failure = (CollectionDataFailure)failureValue;
        await fixture.Complete(CollectionDataParseResult.Reject(failure));

        if (failure == CollectionDataFailure.NoCharacter)
        {
            Assert.Equal(["controller"], fixture.Cleared);
            Assert.Empty(fixture.Published);
            Assert.Equal("Shared collection data skipped because no character is active.", Assert.Single(fixture.Messages));
        }
        else if (failure == CollectionDataFailure.WrongCharacter)
        {
            Assert.Equal(["client", "controller"], fixture.Cleared);
            Assert.Empty(fixture.Published);
            Assert.Equal("Shared collection data was rejected for another character and cleared.", Assert.Single(fixture.Messages));
        }
        else
        {
            Assert.Empty(fixture.Cleared);
            Assert.Equal(failure, Assert.Single(fixture.Published).Failure);
            Assert.Contains(retained ? "Retaining the last accepted Provider 1.2" : "data unavailable", Assert.Single(fixture.Messages));
        }
    }

    /// <summary>Verifies successful publication describes every source and does not repeat log.</summary>
    [Fact]
    public async Task SuccessfulPublicationDescribesEverySourceAndDoesNotRepeatLog()
    {
        using var fixture = new Fixture();
        fixture.Coordinator.SynchronizeCharacter();
        fixture.Coordinator.TryQueueRefresh();
        await fixture.Complete(CollectionDataParseResult.Accept(Snapshot()));
        var message = Assert.Single(fixture.Messages);
        foreach (var source in new[] { "inventory", "armory chest", "chocobo saddlebag", "premium saddlebag", "retainers", "armoire", "glamour dresser" })
            Assert.Contains(source + " Fresh (1 item IDs)", message);
        Assert.Contains("Provider 1.2", message);
        fixture.Coordinator.TryQueueRefresh();
        await fixture.Complete(CollectionDataParseResult.Accept(Snapshot()));
        Assert.Single(fixture.Messages);
        Assert.Equal(2, fixture.Published.Count);
    }

    /// <summary>Verifies character change or shutdown before framework publication rejects result.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CharacterChangeOrShutdownBeforeFrameworkPublicationRejectsResult(bool shutdown)
    {
        using var fixture = new Fixture();
        fixture.Coordinator.SynchronizeCharacter();
        fixture.BeforePublication = () =>
        {
            if (shutdown) fixture.Operations.Dispose();
            else fixture.Owner = 99;
        };
        fixture.Coordinator.TryQueueRefresh();
        await fixture.Complete(CollectionDataParseResult.Accept(Snapshot()));
        Assert.Empty(fixture.Published);
        Assert.Empty(fixture.Messages);
    }

    /// <summary>Verifies refresh failure is observed and queue can recover.</summary>
    [Fact]
    public async Task RefreshFailureIsObservedAndQueueCanRecover()
    {
        using var fixture = new Fixture();
        fixture.Coordinator.SynchronizeCharacter();
        fixture.Coordinator.TryQueueRefresh();
        var failure = new InvalidOperationException("provider");
        fixture.Work.SetException(failure);
        await fixture.Operations.WhenIdle.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Same(failure, Assert.Single(fixture.Errors));
        fixture.Coordinator.TryQueueRefresh();
        await fixture.Complete(CollectionDataParseResult.Accept(Snapshot()));
        Assert.Single(fixture.Published);
    }

    /// <summary>Verifies shutdown cancellation is observed without publishing or logging failure.</summary>
    [Fact]
    public async Task ShutdownCancellationIsObservedWithoutPublishingOrLoggingFailure()
    {
        using var fixture = new Fixture();
        fixture.Coordinator.SynchronizeCharacter();
        fixture.Coordinator.TryQueueRefresh();
        fixture.Operations.Dispose();
        fixture.Work.SetCanceled(fixture.RefreshToken);
        await fixture.Operations.WhenIdle.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Empty(fixture.Published);
        Assert.Empty(fixture.Errors);
        Assert.Empty(fixture.Messages);
    }

    /// <summary>Verifies rejected operation start does not leave refresh latched.</summary>
    [Fact]
    public void RejectedOperationStartDoesNotLeaveRefreshLatched()
    {
        using var fixture = new Fixture();
        fixture.Coordinator.SynchronizeCharacter();
        fixture.Operations.Dispose();
        fixture.Coordinator.TryQueueRefresh();
        fixture.Coordinator.TryQueueRefresh();
        Assert.Equal(2, fixture.Taken);
        Assert.Empty(fixture.Requested);
    }

    /// <summary>Verifies reentrant refresh signal can only win one queue slot.</summary>
    [Fact]
    public async Task ReentrantRefreshSignalCanOnlyWinOneQueueSlot()
    {
        using var fixture = new Fixture();
        fixture.Coordinator.SynchronizeCharacter();
        fixture.OnTake = () =>
        {
            fixture.OnTake = null;
            fixture.Coordinator.TryQueueRefresh();
        };
        fixture.Coordinator.TryQueueRefresh();
        Assert.Single(fixture.Requested);
        await fixture.Complete(CollectionDataParseResult.Accept(Snapshot()));
        Assert.Single(fixture.Published);
    }

    private static CollectionDataSnapshot Snapshot()
    {
        var source = new CollectionSourceSnapshot(CollectionSourceStatus.Fresh, DateTimeOffset.UnixEpoch, [5u], null);
        return new("Provider", "1.2", 42, 1, DateTimeOffset.UnixEpoch, source, source, source, source, source, source, source);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly OwnedOperationRegistry Operations;
        internal readonly CollectionCoordinator Coordinator;
        internal ulong Owner = 42;
        internal bool Pending = true;
        internal int Taken;
        internal CollectionDataSnapshot? Latest;
        internal TaskCompletionSource<CollectionDataParseResult> Work = new();
        internal CancellationToken RefreshToken;
        internal Action? BeforePublication;
        internal Action? OnTake;
        internal readonly List<ulong> Requested = [];
        internal readonly List<CollectionDataParseResult> Published = [];
        internal readonly List<string> Cleared = [];
        internal readonly List<string> Messages = [];
        internal readonly List<Exception> Errors = [];

        internal Fixture()
        {
            Operations = new(Errors.Add);
            Coordinator = new(Operations, () => Owner, () => { Taken++; OnTake?.Invoke(); return Pending; },
                (owner, token) =>
                {
                    Requested.Add(owner);
                    RefreshToken = token;
                    Work = new();
                    return Work.Task;
                }, (action, _) => { BeforePublication?.Invoke(); action(); return Task.CompletedTask; },
                () => Latest, () => Cleared.Add("client"), () => Cleared.Add("controller"), Published.Add,
                () => Cleared.Add("preview"), Messages.Add);
        }

        internal async Task Complete(CollectionDataParseResult result)
        {
            Work.SetResult(result);
            await Operations.WhenIdle.WaitAsync(TimeSpan.FromSeconds(1));
        }

        /// <summary>Verifies dispose.</summary>
        public void Dispose() => Operations.Dispose();
    }
}
