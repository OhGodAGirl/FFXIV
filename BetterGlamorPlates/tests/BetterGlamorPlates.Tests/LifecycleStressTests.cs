namespace BetterGlamorPlates.Tests;

using System.Reflection;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;
using BetterGlamorPlates.Shared.CollectionData;
using BetterGlamorPlates.Shared.Operations;

/// <summary>
/// Exercises repeated teardown, partial initialization, cancellation races,
/// stale commands, and disposal while queued work is incomplete.
/// </summary>
public sealed class LifecycleStressTests
{
    // This bounds a hung test, not worker latency; hosted runners may defer continuations.
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Verifies repeated teardown is reverse-ordered and idempotent.</summary>
    [Fact]
    public void TeardownIsReverseOrderedAndIdempotentAcrossRepeatedLifecycles()
    {
        for (var iteration = 0; iteration < 250; iteration++)
        {
            var released = new List<int>();
            var errors = new List<Exception>();
            var teardown = new OwnedTeardown(errors.Add);
            teardown.Add(() => released.Add(1));
            teardown.Add(() => released.Add(2));
            teardown.Add(() => released.Add(3));

            teardown.Dispose();
            teardown.Dispose();

            Assert.Equal([3, 2, 1], released);
            Assert.Empty(errors);
        }
    }

    /// <summary>Verifies partial initialization cleanup continues after a failure.</summary>
    [Fact]
    public void PartialInitializationCleanupContinuesAfterReleaseFailure()
    {
        var released = new List<int>();
        var errors = new List<Exception>();
        var teardown = new OwnedTeardown(errors.Add);
        teardown.Add(() => released.Add(1));
        teardown.Add(() => throw new InvalidOperationException("release failed"));
        teardown.Add(() => released.Add(3));

        teardown.Dispose();

        Assert.Equal([3, 1], released);
        Assert.Single(errors);
    }

    /// <summary>Verifies teardown rejects invalid registrations and releases callbacks.</summary>
    [Fact]
    public void TeardownRejectsNullAndLateRegistrationsAndClearsOwnership()
    {
        var teardown = new OwnedTeardown(_ => { });
        Assert.Throws<ArgumentNullException>(() => teardown.Add(null!));
        teardown.Add(() => { });

        var field = typeof(OwnedTeardown).GetField(
            "releaseActions",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var actions = Assert.IsType<List<Action>>(field!.GetValue(teardown));
        Assert.Single(actions);

        teardown.Dispose();

        Assert.Empty(actions);
        Assert.Throws<ObjectDisposedException>(() => teardown.Add(() => { }));
    }

    /// <summary>Verifies disposal cancels and observes incomplete work.</summary>
    [Fact]
    public async Task DisposalCancelsActiveWorkAndObservesCompletion()
    {
        var errors = new List<Exception>();
        var registry = new OwnedOperationRegistry(errors.Add);
        CancellationToken capturedToken = default;
        Task? scheduled = null;
        Assert.True(registry.TryStart(token =>
        {
            capturedToken = token;
            scheduled = Task.Delay(Timeout.InfiniteTimeSpan, token);
            return scheduled;
        }));

        registry.Dispose();
        registry.Dispose();
        await registry.WhenIdle.WaitAsync(CompletionTimeout);

        Assert.True(capturedToken.IsCancellationRequested);
        Assert.NotNull(scheduled);
        Assert.True(scheduled.IsCanceled);
        Assert.Equal(0, registry.ActiveCount);
        Assert.Empty(errors);
        Assert.False(registry.TryStart(_ => Task.CompletedTask));
        var field = typeof(OwnedOperationRegistry).GetField(
            "shutdown",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var shutdown = Assert.IsType<CancellationTokenSource>(field!.GetValue(registry));
        Assert.Throws<ObjectDisposedException>(() => _ = shutdown.Token);
    }

    /// <summary>Verifies one fault is observed exactly once.</summary>
    [Fact]
    public async Task FaultedWorkIsObservedExactlyOnce()
    {
        var errors = new List<Exception>();
        using var registry = new OwnedOperationRegistry(errors.Add);

        Assert.True(registry.TryStart(_ =>
            Task.FromException(new InvalidOperationException("operation failed"))));
        await registry.WhenIdle.WaitAsync(CompletionTimeout);

        var error = Assert.Single(errors);
        Assert.Equal("operation failed", error.Message);
    }

    /// <summary>Verifies active ownership and the idle transition are observable.</summary>
    [Fact]
    public async Task ActiveWorkIsTrackedUntilItsCompletionCallbackRuns()
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var registry = new OwnedOperationRegistry(_ => { });

        Assert.Throws<ArgumentNullException>(() => registry.TryStart(null!));
        Assert.True(registry.WhenIdle.IsCompletedSuccessfully);
        Assert.True(registry.TryStart(_ => completion.Task));
        Assert.Equal(1, registry.ActiveCount);
        Assert.False(registry.WhenIdle.IsCompleted);

        completion.SetResult();
        await registry.WhenIdle.WaitAsync(CompletionTimeout);

        Assert.Equal(0, registry.ActiveCount);
    }

    /// <summary>Verifies registry error reporting is an explicit required dependency.</summary>
    [Fact]
    public void RegistryRejectsNullErrorReporter()
    {
        Assert.Throws<ArgumentNullException>(() => new OwnedOperationRegistry(null!));

        using var registry = new OwnedOperationRegistry(_ => { });
        Assert.Throws<ArgumentNullException>(() => registry.AddDisposeWhenIdle(null!));
    }

    /// <summary>Verifies idle disposal releases the owned cancellation source.</summary>
    [Fact]
    public void IdleDisposalReleasesTheCancellationSourceImmediately()
    {
        var errors = new List<Exception>();
        var registry = new OwnedOperationRegistry(errors.Add);
        var field = typeof(OwnedOperationRegistry).GetField(
            "shutdown",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var shutdown = Assert.IsType<CancellationTokenSource>(field!.GetValue(registry));
        _ = shutdown.Token;
        var released = false;
        registry.AddDisposeWhenIdle(() => released = true);
        var releasesField = typeof(OwnedOperationRegistry).GetField(
            "releasesWhenIdle",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var releases = Assert.IsType<List<Action>>(releasesField!.GetValue(registry));
        Assert.Single(releases);

        registry.Dispose();
        registry.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = shutdown.Token);
        Assert.True(released);
        Assert.Empty(releases);
        Assert.False(registry.TryStart(_ => Task.CompletedTask));
        Assert.Empty(errors);
    }

    /// <summary>Verifies a synchronously throwing scheduler is reported and rejected.</summary>
    [Fact]
    public void SynchronousSchedulerFailureIsReportedAndRejected()
    {
        var errors = new List<Exception>();
        using var registry = new OwnedOperationRegistry(errors.Add);

        var started = registry.TryStart(_ =>
            throw new InvalidOperationException("scheduler failed"));

        Assert.False(started);
        Assert.Equal(0, registry.ActiveCount);
        Assert.True(registry.WhenIdle.IsCompletedSuccessfully);
        Assert.Equal("scheduler failed", Assert.Single(errors).Message);
    }

    /// <summary>Verifies null tasks are rejected through the owned failure channel.</summary>
    [Fact]
    public void NullScheduledTaskIsReportedAndRejected()
    {
        var errors = new List<Exception>();
        using var registry = new OwnedOperationRegistry(errors.Add);

        Assert.False(registry.TryStart(_ => null!));
        var error = Assert.IsType<InvalidOperationException>(Assert.Single(errors));
        Assert.Equal("Owned operation returned no task.", error.Message);
        Assert.Equal(0, registry.ActiveCount);
        Assert.True(registry.WhenIdle.IsCompletedSuccessfully);
    }

    /// <summary>Verifies idle late registrations execute immediately and report release failures.</summary>
    [Fact]
    public void LateReleaseRunsImmediatelyAndReportsFailure()
    {
        var errors = new List<Exception>();
        var released = 0;
        var registry = new OwnedOperationRegistry(errors.Add);
        registry.Dispose();

        registry.AddDisposeWhenIdle(() => released++);
        registry.AddDisposeWhenIdle(() => throw new InvalidOperationException("late release failed"));

        Assert.Equal(1, released);
        Assert.Equal("late release failed", Assert.Single(errors).Message);
    }

    /// <summary>Verifies idle completion waits for every concurrently owned operation.</summary>
    [Fact]
    public async Task MultipleOperationsReleaseIdleOnlyAfterTheLastCompletion()
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registry = new OwnedOperationRegistry(_ => { });
        Assert.True(registry.TryStart(_ => first.Task));
        Assert.True(registry.TryStart(_ => second.Task));
        Assert.Equal(2, registry.ActiveCount);

        first.SetResult();
        // Yield alone does not establish that the registry's asynchronous completion ran.
        using var completionDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (registry.ActiveCount == 2)
            await Task.Delay(1, completionDeadline.Token);
        Assert.Equal(1, registry.ActiveCount);
        Assert.False(registry.WhenIdle.IsCompleted);

        second.SetResult();
        await registry.WhenIdle.WaitAsync(CompletionTimeout);
        Assert.Equal(0, registry.ActiveCount);
    }

    /// <summary>Verifies blocked work retains dependent disposal until completion is observed.</summary>
    [Fact]
    public async Task BlockedRefreshRetainsDeferredDependencyDisposalUntilObservedIdle()
    {
        var releaseWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registry = new OwnedOperationRegistry(_ => { });
        registry.AddDisposeWhenIdle(() => released.SetResult());

        Assert.True(registry.TryStart(_ => releaseWork.Task));

        registry.Dispose();

        Assert.False(releaseWork.Task.IsCompleted);
        Assert.False(released.Task.IsCompleted);
        Assert.False(registry.WhenIdle.IsCompleted);

        releaseWork.SetResult();
        await registry.WhenIdle.WaitAsync(CompletionTimeout);

        // Idle describes active work; release callbacks run immediately after that signal.
        await released.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(released.Task.IsCompletedSuccessfully);
    }

    /// <summary>Verifies stale, cancelled, or drifted shared refreshes cannot publish.</summary>
    [Fact]
    public void SharedCollectionPublicationRejectsStaleCancellationAndContentDrift()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            Assert.True(CollectionProviderPolicy.CanPublishBackgroundRefresh(42, 42, CollectionDataFailure.None, false));
            Assert.False(CollectionProviderPolicy.CanPublishBackgroundRefresh(42, 0, CollectionDataFailure.None, false));
            Assert.False(CollectionProviderPolicy.CanPublishBackgroundRefresh(0, 42, CollectionDataFailure.None, false));
            Assert.False(CollectionProviderPolicy.CanPublishBackgroundRefresh(42, 99, CollectionDataFailure.None, false));
            Assert.False(CollectionProviderPolicy.CanPublishBackgroundRefresh(42, 42, CollectionDataFailure.Busy, false));
            Assert.False(CollectionProviderPolicy.CanPublishBackgroundRefresh(42, 42, CollectionDataFailure.Disposed, false));
            Assert.False(CollectionProviderPolicy.CanPublishBackgroundRefresh(42, 42, CollectionDataFailure.StaleCompletion, false));
            Assert.False(CollectionProviderPolicy.CanPublishBackgroundRefresh(42, 42, CollectionDataFailure.None, true));
        }
    }

    /// <summary>Verifies stale completions cannot mutate disposed state.</summary>
    [Fact]
    public void StaleCompletionCannotMutateDisposedController()
    {
        var store = new FakePlateLibraryStore();
        var controller = new MainController(
            store,
            new FakeNativeCollectionValidator(),
            new FakeSharedCollectionSnapshotSource(),
            new FakePlateEditor(),
            new FakeItemFacts());
        controller.Dispose();

        for (var signal = 0; signal < 100; signal++)
        {
            controller.Execute(
                new MainCommand.CreatePlate(),
                TestDomainValues.Timestamp.AddSeconds(signal),
                Guid.NewGuid());
        }

        Assert.Empty(store.State.SavedPlates);
        Assert.Equal(0, store.SaveCount);
    }

    /// <summary>Verifies disposed controllers ignore late shared-data updates and clears.</summary>
    [Fact]
    public void SharedCollectionUpdatesCannotMutateDisposedController()
    {
        using var controller = new MainController(
            new FakePlateLibraryStore(),
            new FakeNativeCollectionValidator(),
            new FakeSharedCollectionSnapshotSource(),
            new FakePlateEditor(),
            new FakeItemFacts());
        var initial = controller.View;
        var shared = TestDomainValues.SharedSnapshot();
        controller.Dispose();

        controller.UpdateSharedCollectionData(CollectionDataParseResult.Accept(shared));
        controller.ClearSharedCollectionData();

        Assert.Equal(initial, controller.View);
    }
}
