using BetterGlamorPlates.Adapters.CollectionData;
using BetterGlamorPlates.Shared.CollectionData;
using Kit = Exportaramus.ConsumerKit.V1_2;

namespace BetterGlamorPlates.Tests.Features.GlamourPlates;

/// <summary>Exercises production decisions with controlled inputs and observable assertions.</summary>
public sealed class ExportaramusConsumerLifecycleTests
{
    private const ulong Character = 12345678901234567;
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(2);
    private static readonly DateTimeOffset Start = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Verifies the behavior named by this regression with deterministic inputs.</summary>
    [Fact]
    public async Task PollsAtTheExactDeadlineAndCoalescesExplicitRequests()
    {
        var now = Start;
        using var consumer = new ExportaramusConsumer((_, _) => new Transport(), _ => { }, () => now);
        Assert.True(consumer.TryTakeRefreshRequest());
        Assert.False(consumer.TryTakeRefreshRequest());
        now = now.AddSeconds(5).AddTicks(-1);
        Assert.False(consumer.TryTakeRefreshRequest());
        now = now.AddTicks(1);
        Assert.True(consumer.TryTakeRefreshRequest());
        Assert.False(consumer.TryTakeRefreshRequest());

        consumer.RequestRefresh();
        consumer.RequestRefresh();

        Assert.True(consumer.TryTakeRefreshRequest());
        Assert.False(consumer.TryTakeRefreshRequest());
        consumer.Dispose();
        consumer.RequestRefresh();
        consumer.ClearCharacter();
        Assert.False(consumer.TryTakeRefreshRequest());
        Assert.Equal(CollectionDataFailure.Disposed, (await consumer.RefreshAsync(Character, now).WaitAsync(CompletionTimeout)).Failure);
        await consumer.Disposal.WaitAsync(CompletionTimeout);
    }

    /// <summary>Verifies the behavior named by this regression with deterministic inputs.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoticesInvalidateImmediatelyAndRejectAlreadyRunningCompletion(bool withdrawal)
    {
        Action<string>? changed = null;
        Action<string>? withdrawn = null;
        var worker = new Worker();
        var transport = new Transport();
        using var consumer = new ExportaramusConsumer((a, b) => { changed = a; withdrawn = b; return transport; },
            _ => { }, () => Start, worker.Start);
        var initial = consumer.RefreshAsync(Character, Start);
        worker.Complete();
        Assert.True((await initial.WaitAsync(CompletionTimeout)).Accepted);
        Assert.NotNull(consumer.Latest);
        consumer.TryTakeRefreshRequest();
        consumer.RequestRefresh();
        consumer.TryTakeRefreshRequest();
        var refresh = consumer.RefreshAsync(Character, Start);
        var duplicate = consumer.RefreshAsync(Character, Start);
        Assert.Equal(2, worker.Count);

        (withdrawal ? withdrawn : changed)!("{}");

        Assert.Null(consumer.Latest);
        Assert.True(consumer.TryTakeRefreshRequest());
        worker.Complete();
        Assert.Equal(CollectionDataFailure.StaleCompletion, (await refresh.WaitAsync(CompletionTimeout)).Failure);
        Assert.Equal(CollectionDataFailure.StaleCompletion, (await duplicate.WaitAsync(CompletionTimeout)).Failure);
        Assert.Null(consumer.Latest);
        consumer.Dispose();
        await consumer.Disposal.WaitAsync(CompletionTimeout);
        changed!("{}");
        withdrawn!("{}");
        Assert.False(consumer.TryTakeRefreshRequest());
        Assert.Null(consumer.Latest);
        Assert.Equal(1, transport.DisposeCount);
    }

    /// <summary>Verifies the behavior named by this regression with deterministic inputs.</summary>
    [Fact]
    public async Task DisposalWaitsForOwnedWorkAndRejectsItsProjection()
    {
        var worker = new Worker();
        var transport = new Transport();
        var consumer = new ExportaramusConsumer((_, _) => transport, _ => { }, () => Start, worker.Start);
        var refresh = consumer.RefreshAsync(Character, Start);

        consumer.Dispose();
        var disposal = consumer.Disposal;
        consumer.Dispose();

        Assert.Same(disposal, consumer.Disposal);
        Assert.False(disposal.IsCompleted);
        Assert.Equal(0, transport.DisposeCount);
        worker.Complete();
        await disposal.WaitAsync(CompletionTimeout);
        Assert.Equal(CollectionDataFailure.StaleCompletion, (await refresh.WaitAsync(CompletionTimeout)).Failure);
        Assert.Null(consumer.Latest);
        Assert.Equal(1, transport.DisposeCount);
    }

    /// <summary>Verifies the behavior named by this regression with deterministic inputs.</summary>
    [Fact]
    public async Task CharacterClearTracksIdentityWorkAndNeverRestoresThePreviousCharacter()
    {
        var worker = new Worker();
        using var consumer = new ExportaramusConsumer((_, _) => new Transport(), _ => { }, () => Start, worker.Start);
        var first = consumer.RefreshAsync(Character, Start);
        worker.Complete();
        Assert.True((await first.WaitAsync(CompletionTimeout)).Accepted);

        consumer.ClearCharacter();
        consumer.ClearCharacter();

        Assert.Null(consumer.Latest);
        Assert.Equal(2, worker.Count);
        consumer.Dispose();
        Assert.False(consumer.Disposal.IsCompleted);
        worker.Complete();
        await consumer.Disposal.WaitAsync(CompletionTimeout);
        Assert.Null(consumer.Latest);
    }

    /// <summary>Verifies the behavior named by this regression with deterministic inputs.</summary>
    [Fact]
    public async Task IdentityAndDisposalFailuresAreObserved()
    {
        var failure = new InvalidOperationException("worker failed");
        var errors = new List<Exception>();
        var consumer = new ExportaramusConsumer((_, _) => new Transport(), errors.Add,
            () => Start, (_, _) => Task.FromException(failure));

        consumer.ClearCharacter();
        consumer.Dispose();
        await consumer.Disposal.WaitAsync(CompletionTimeout);

        Assert.Equal(2, errors.Count);
        Assert.All(errors, error => Assert.Same(failure, error));
    }

    /// <summary>Verifies the behavior named by this regression with deterministic inputs.</summary>
    [Fact]
    public async Task TransportDisposeFailureIsReportedOnce()
    {
        var failure = new InvalidOperationException("dispose failed");
        var errors = new List<Exception>();
        var transport = new Transport { DisposeFailure = failure };
        var consumer = new ExportaramusConsumer((_, _) => transport, errors.Add, () => Start);
        consumer.Dispose();
        await consumer.Disposal.WaitAsync(CompletionTimeout);
        consumer.Dispose();
        Assert.Same(failure, Assert.Single(errors));
        Assert.Equal(1, transport.DisposeCount);
    }

    /// <summary>Notifications must reach the pinned client, including before its next discovery deadline.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoticeForwardingForcesActualRefreshBeforeDiscoveryDeadline(bool withdrawal)
    {
        var worker = new Worker();
        Action<string>? notice = null;
        using var consumer = new ExportaramusConsumer((changed, withdrawn) =>
        {
            notice = withdrawal ? withdrawn : changed;
            return new Transport();
        }, _ => { }, () => Start, worker.Start);
        var first = consumer.RefreshAsync(Character, Start);
        worker.Complete();
        Assert.True((await first.WaitAsync(CompletionTimeout)).Accepted);

        notice!(withdrawal ? File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid", "snapshot-withdrawn-valid.json")) : "{}");
        var refresh = consumer.RefreshAsync(Character, Start.AddSeconds(1));

        Assert.Equal(2, worker.Count);
        Assert.False(refresh.IsCompleted);
        worker.Complete();
        Assert.True((await refresh.WaitAsync(CompletionTimeout)).Accepted);
        consumer.Dispose();
        await consumer.Disposal.WaitAsync(CompletionTimeout);
    }

    /// <summary>Explicit refresh and completion never rely on the caller's synchronization context.</summary>
    [Theory]
    [InlineData("refresh")]
    [InlineData("identity")]
    [InlineData("dispose")]
    public async Task BackgroundLifecycleNeverPostsToCallingContext(string operation)
    {
        var worker = new Worker();
        var context = new CountingContext();
        var consumer = new ExportaramusConsumer((_, _) => new Transport(), _ => { }, () => Start, worker.Start);
        Task work;
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            if (operation == "identity")
            {
                consumer.ClearCharacter();
                consumer.Dispose();
                work = consumer.Disposal;
            }
            else
            {
                work = consumer.RefreshAsync(Character, Start);
                if (operation == "dispose")
                {
                    consumer.Dispose();
                    work = Task.WhenAll(work, consumer.Disposal);
                }
            }
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }

        worker.Complete();
        await work.WaitAsync(CompletionTimeout);
        consumer.Dispose();
        await consumer.Disposal.WaitAsync(CompletionTimeout);

        Assert.Equal(0, context.Posts);
    }

    /// <summary>Disposal prevents later character-clear clock reads and task creation.</summary>
    [Fact]
    public async Task DisposedCharacterClearDoesNotReadTheClock()
    {
        var reads = 0;
        var consumer = new ExportaramusConsumer((_, _) => new Transport(), _ => { }, () => { reads++; return Start; });
        consumer.Dispose();
        await consumer.Disposal.WaitAsync(CompletionTimeout);

        consumer.ClearCharacter();

        Assert.Equal(0, reads);
    }

    /// <summary>Disposal awaits identity error observation even when the kit has finished cancelled work.</summary>
    [Fact]
    public async Task DisposalAwaitsIdentityObservationWithoutCapturingContext()
    {
        var worker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var context = new CountingContext();
        var consumer = new ExportaramusConsumer((_, _) => new Transport(), _ =>
        {
            observing.SetResult();
            Assert.True(release.Wait(CompletionTimeout), "Identity observation was not released.");
        }, () => Start, (_, _) => worker.Task);
        consumer.ClearCharacter();
        worker.SetCanceled();
        bool completedBeforeObservation;
        var previous = SynchronizationContext.Current;
        try
        {
            await observing.Task.WaitAsync(CompletionTimeout);
            SynchronizationContext.SetSynchronizationContext(context);
            consumer.Dispose();
            completedBeforeObservation = consumer.Disposal.IsCompleted;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            release.Set();
        }
        await consumer.Disposal.WaitAsync(CompletionTimeout);

        Assert.False(completedBeforeObservation);
        Assert.Equal(0, context.Posts);
    }

    /// <summary>Once disposal begins, no request may reach a client whose asynchronous cleanup is still pending.</summary>
    [Fact]
    public async Task DisposedConsumerNeverCallsClientEvenBeforeClientCleanupCompletes()
    {
        var port = new ControlledClient();
        var clockReads = 0;
        var consumer = new ExportaramusConsumer(port, _ => throw new InvalidOperationException("Unexpected failure"),
            () => { clockReads++; return Start; });
        consumer.RequestRefresh();
        Assert.Equal(1, port.Notices);
        consumer.Dispose();
        Assert.False(consumer.Disposal.IsCompleted);

        try
        {
            consumer.RequestRefresh();
            consumer.ClearCharacter();
            var result = await consumer.RefreshAsync(Character, Start).WaitAsync(CompletionTimeout);

            Assert.Equal(CollectionDataFailure.Disposed, result.Failure);
            Assert.Equal(1, port.Notices);
            Assert.Equal(0, port.Ticks);
            Assert.Equal(0, clockReads);
        }
        finally
        {
            port.Cleanup.SetResult();
        }
        await consumer.Disposal.WaitAsync(CompletionTimeout);
        consumer.RequestRefresh();
        Assert.Equal(1, port.Notices);
        Assert.Equal(1, port.Disposals);
    }

    private sealed class ControlledClient : IExportaramusClientPort
    {
        internal TaskCompletionSource Cleanup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Notices { get; private set; }
        internal int Ticks { get; private set; }
        internal int Disposals { get; private set; }
        public Kit.CollectionDataClientView View => throw new InvalidOperationException("Disposed client read");
        public void Changed(string payload) => Notices++;
        public Task TickAsync(string? contentId, DateTimeOffset now) { Ticks++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposals++; return new ValueTask(Cleanup.Task); }
    }

    private sealed class CountingContext : SynchronizationContext
    {
        private int posts;
        internal int Posts => Volatile.Read(ref posts);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref posts);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
    }

    private sealed class Worker
    {
        private Action? action;
        private TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Count { get; private set; }
        internal Task Start(Action callback, CancellationToken token)
        {
            Count++;
            action = callback;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return completion.Task;
        }
        internal void Complete()
        {
            action!();
            completion.SetResult();
        }
    }

    private sealed class Transport : Kit.ICollectionDataTransport
    {
        internal int DisposeCount { get; private set; }
        internal Exception? DisposeFailure { get; init; }
        public string ReadCapabilities() => Read("capabilities-valid.json");
        public string ReadSnapshot() => Read("compact-valid-full.json");
        public string Report(string contentId, uint itemId, string reporter) => throw new InvalidOperationException("Read-only consumer");
        public void Dispose()
        {
            DisposeCount++;
            if (DisposeFailure is not null) throw DisposeFailure;
        }
        private static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid", name));
    }
}




