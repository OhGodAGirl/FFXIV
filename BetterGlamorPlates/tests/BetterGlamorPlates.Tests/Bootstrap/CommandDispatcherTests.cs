using BetterGlamorPlates.Bootstrap;
using BetterGlamorPlates.Features.Main;
using BetterGlamorPlates.Shared.Operations;

namespace BetterGlamorPlates.Tests.Bootstrap;

/// <summary>Verifies command dispatcher tests.</summary>
public sealed class CommandDispatcherTests
{
    /// <summary>Disposal stops new commands and cancels an existing request without leaking queued state.</summary>
    [Fact]
    public async Task DisposedDispatcherRejectsNewWork()
    {
        using var fixture = new Fixture();
        fixture.Dispatcher.Queue(new MainCommand.LoadActive());
        fixture.Dispatcher.Dispose();
        fixture.Dispatcher.Dispose();
        fixture.Dispatcher.Queue(new MainCommand.CreatePlate());
        Assert.Single(fixture.Scheduled);
        await fixture.RunNext();
        Assert.Empty(fixture.Executed);
        Assert.Equal([true, false], fixture.LoadStates);
        Assert.Empty(fixture.Scheduled);
    }
    /// <summary>Verifies native writes are coalesced and can be queued again after completion.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeWritesAreCoalescedAndCanBeQueuedAgainAfterCompletion(bool clear)
    {
        using var fixture = new Fixture();
        MainCommand command = clear ? new MainCommand.ClearOutfitItems(Guid.NewGuid(), 1) : new MainCommand.LoadActive();

        fixture.Dispatcher.Queue(command);
        fixture.Dispatcher.Queue(command);
        Assert.Single(fixture.Scheduled);
        Assert.Equal([true], fixture.LoadStates);
        await fixture.RunNext();
        fixture.Dispatcher.Queue(command);
        await fixture.RunNext();

        Assert.Equal([command, command], fixture.Executed);
        Assert.Equal([true, false, true, false], fixture.LoadStates);
        Assert.Equal(2, fixture.Cancellations.Count);
        foreach (var cancellation in fixture.Cancellations)
            Assert.Throws<ObjectDisposedException>(() => cancellation.Token);
    }

    /// <summary>Verifies command executes only for the captured nonzero character.</summary>
    [Theory]
    [InlineData(0, 42, false)]
    [InlineData(42, 43, false)]
    [InlineData(42, 42, true)]
    public async Task CommandExecutesOnlyForTheCapturedNonzeroCharacter(ulong owner, ulong current, bool expected)
    {
        using var fixture = new Fixture { Owner = owner };
        var command = new MainCommand.CreatePlate();
        fixture.Dispatcher.Queue(command);

        fixture.Owner = current;
        await fixture.RunNext();

        Assert.Equal(expected ? [command] : [], fixture.Executed);
        Assert.Empty(fixture.LoadStates);
    }

    /// <summary>Verifies scheduler that runs acancelled callback cannot execute it.</summary>
    [Fact]
    public async Task SchedulerThatRunsACancelledCallbackCannotExecuteIt()
    {
        using var fixture = new Fixture();
        fixture.Dispatcher.Queue(new MainCommand.LoadActive());
        fixture.Dispatcher.CancelLoad();
        await fixture.RunNext();
        Assert.Empty(fixture.Executed);
        Assert.Equal([true, false], fixture.LoadStates);
        Assert.Equal(0, fixture.CancelledReports);
    }

    /// <summary>Verifies user cancellation reports once on the scheduler and releases the queue.</summary>
    [Fact]
    public async Task UserCancellationReportsOnceOnTheSchedulerAndReleasesTheQueue()
    {
        using var fixture = new Fixture();
        fixture.Dispatcher.Queue(new MainCommand.LoadActive());
        var work = fixture.Scheduled.Dequeue();
        fixture.Dispatcher.CancelLoad();
        fixture.Dispatcher.CancelLoad();
        work.Completion.SetCanceled(work.Token);
        // Continuations are deliberately inline: the report is scheduled before this assertion.
        Assert.Single(fixture.Scheduled);
        await fixture.RunNext();
        Assert.Equal(1, fixture.CancelledReports);
        Assert.Empty(fixture.Executed);
        Assert.Equal([true, false], fixture.LoadStates);
        fixture.Dispatcher.CancelLoad();
    }

    /// <summary>Verifies shutdown suppresses writes and cancellation reports.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShutdownSuppressesWritesAndCancellationReports(bool load)
    {
        using var fixture = new Fixture();
        fixture.Dispatcher.Queue(load ? new MainCommand.LoadActive() : new MainCommand.CreatePlate());
        var work = fixture.Scheduled.Dequeue();
        fixture.Operations.Dispose();

        work.Completion.SetCanceled(work.Token);
        Assert.Empty(fixture.Scheduled);
        await fixture.Operations.WhenIdle.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Empty(fixture.Executed);
        Assert.Empty(fixture.Scheduled);
        Assert.Equal(0, fixture.CancelledReports);
        Assert.Empty(fixture.Errors);
        Assert.Equal(load ? [true, false] : [], fixture.LoadStates);
    }

    /// <summary>Verifies rejected start clears queued state and disposes cancellation.</summary>
    [Fact]
    public void RejectedStartClearsQueuedStateAndDisposesCancellation()
    {
        using var fixture = new Fixture();
        fixture.Operations.Dispose();
        fixture.Dispatcher.Queue(new MainCommand.LoadActive());
        fixture.Dispatcher.CancelLoad();
        fixture.Dispatcher.Queue(new MainCommand.LoadActive());
        fixture.Dispatcher.Queue(new MainCommand.CreatePlate());
        Assert.Equal([true, false, true, false], fixture.LoadStates);
        Assert.Empty(fixture.Scheduled);
    }

    /// <summary>Verifies unrequested failure is observed and load queue recovers.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnrequestedFailureIsObservedAndLoadQueueRecovers(bool cancellationException)
    {
        using var fixture = new Fixture();
        fixture.Dispatcher.Queue(new MainCommand.LoadActive());
        var work = fixture.Scheduled.Dequeue();
        Exception failure = cancellationException ? new OperationCanceledException() : new InvalidOperationException("failed");
        work.Completion.SetException(failure);
        await fixture.Operations.WhenIdle.WaitAsync(TimeSpan.FromSeconds(1));

        if (cancellationException) Assert.Empty(fixture.Errors);
        else Assert.Same(failure, Assert.Single(fixture.Errors));
        Assert.Equal(0, fixture.CancelledReports);
        Assert.Equal([true, false], fixture.LoadStates);
        fixture.Dispatcher.Queue(new MainCommand.LoadActive());
        await fixture.RunNext();
        Assert.Single(fixture.Executed);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly Queue<Work> Scheduled = new();
        internal readonly List<MainCommand> Executed = [];
        internal readonly List<bool> LoadStates = [];
        internal readonly List<Exception> Errors = [];
        internal readonly List<CancellationTokenSource> Cancellations = [];
        internal readonly OwnedOperationRegistry Operations;
        internal readonly CommandDispatcher Dispatcher;
        internal ulong Owner = 42;
        internal int CancelledReports;

        internal Fixture()
        {
            Operations = new(Errors.Add);
            Dispatcher = new(Operations, (action, token) =>
            {
                var work = new Work(action, token);
                Scheduled.Enqueue(work);
                return work.Completion.Task;
            }, () => Owner, Executed.Add, LoadStates.Add, () => CancelledReports++, () =>
            {
                var cancellation = new CancellationTokenSource();
                Cancellations.Add(cancellation);
                return cancellation;
            });
        }

        internal async Task RunNext()
        {
            var work = Scheduled.Dequeue();
            work.Action();
            work.Completion.SetResult();
            await Operations.WhenIdle.WaitAsync(TimeSpan.FromSeconds(1));
        }

        /// <summary>Verifies dispose.</summary>
        public void Dispose() => Operations.Dispose();
    }

    private sealed record Work(Action Action, CancellationToken Token)
    {
        internal TaskCompletionSource Completion { get; } = new();
    }
}
