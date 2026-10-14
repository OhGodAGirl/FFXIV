using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class PromptPublicationTests
{
    [Fact]
    public async Task UnchangedTargetedRetryClearsOnlyThePriorCaptureFailure()
    {
        await using var run = new Harness(); await run.Initialize();
        run.Port.TargetedSupported = false;
        run.Controller.HintSource("orchestrions", 123, 19);
        run.Port.TargetedSupported = true;
        run.Port.FailNextRead = true;
        run.Controller.HintSource("orchestrions", 123, 20);
        run.Update(20.25); run.Update(20.25);
        Assert.NotNull(run.Controller.Error);
        var accepted = run.Controller.Publication.Current;
        await run.Cycle(21.25);
        Assert.Null(run.Controller.Error);
        Assert.Same(accepted, run.Controller.Publication.Current);
        run.Controller.Dispose();
        run.Controller.HintSource("orchestrions", 123, 22);
        Assert.Null(run.Controller.Publication.Current);
    }

    [Fact]
    public async Task QueuedConsumerReportIsServicedBeforeContinuousCaptureHints()
    {
        await using var run = new Harness(); await run.Initialize();
        Assert.Equal("Accepted", run.Controller.Reports.TryEnqueue("123", 100, "Fixture", 20, SnapshotFixture.Now.AddSeconds(20)));
        for (var i = 0; i < 1000; i++) run.Controller.HintSource("orchestrions", 123, 20 + i / 10000d);
        run.Update(20.25);
        Assert.Equal(0, run.Controller.Reports.Count);
        var deadline = Stopwatch.StartNew();
        while (!run.Controller.Publication.Current!.Data.State.ReportedEvidence.Items.Any(item => item.ItemId == 100 && item.Reporter == "Fixture"))
        {
            Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(5), run.Controller.Error);
            run.Controller.HintSource("orchestrions", 123, 20.25);
            await Task.Delay(1); run.Update(20.25);
        }
        Assert.Null(run.Controller.Error);
    }

    [Fact]
    public async Task TargetedHealthFailureAndRecoveryPublishWithoutLosingRetainedUnlocks()
    {
        await using var run = new Harness();
        run.Port.State = SnapshotFixture.Create().State;
        await run.Initialize();
        var healthy = run.Port.State.Unlocks["orchestrions"];
        run.Port.State = run.Port.State with { Unlocks = run.Port.State.Unlocks.SetItem("orchestrions", new([], CaptureReadPolicy.Health(DataType.PermanentUnlock, 2, SnapshotFixture.Now.AddSeconds(20), ReasonCode.NotLoadedThisSession, true))) };
        run.Controller.HintSource("orchestrions", 123, 20); await run.Cycle(20.25);
        var retained = run.Controller.Publication.Current!.Data.State.Unlocks["orchestrions"];
        Assert.Equal(new uint[] { 1 }, retained.UnlockedIds);
        Assert.Equal(SourceStatus.Stale, retained.Health.SourceStatus);
        Assert.Equal(ReasonCode.NotLoadedThisSession, retained.Health.ReasonCode);
        Assert.Equal(healthy.Health.ObservedAtUtc, retained.Health.ObservedAtUtc);
        run.Port.State = run.Port.State with { Unlocks = run.Port.State.Unlocks.SetItem("orchestrions", healthy with { Health = healthy.Health with { ObservedAtUtc = SnapshotFixture.Now.AddSeconds(21), CaptureGeneration = 3 } }) };
        await run.Cycle(21.25);
        var recovered = run.Controller.Publication.Current!.Data.State.Unlocks["orchestrions"];
        Assert.Equal(SourceStatus.Fresh, recovered.Health.SourceStatus);
        Assert.Equal(SnapshotFixture.Now.AddSeconds(21), recovered.Health.ObservedAtUtc);
        Assert.Equal(new uint[] { 1 }, recovered.UnlockedIds);
    }

    [Fact]
    public async Task TargetedCaptureDoesNotPostponeEitherScheduledDeadline()
    {
        await using var run = new Harness(); await run.Initialize();
        run.Port.State = run.Port.State with { Unlocks = run.Port.State.Unlocks.SetItem("orchestrions", run.Port.State.Unlocks["orchestrions"] with { UnlockedIds = [1] }) };
        run.Controller.HintSource("orchestrions", 123, 100); await run.Cycle(100.25);
        await run.Cycle(304.999);
        Assert.Equal(new[] { (true, 5d) }, run.Port.Scheduled);
        await run.Cycle(305);
        Assert.Equal(new[] { (true, 5d), (false, 305d) }, run.Port.Scheduled);
        await run.Cycle(1805);
        Assert.Equal((true, 1805d), run.Port.Scheduled.Last());
    }

    [Fact]
    public async Task CompletedTargetedWorkerCannotRepublishAfterLogout()
    {
        await using var run = new Harness(); await run.Initialize();
        run.Port.State = run.Port.State with { Unlocks = run.Port.State.Unlocks.SetItem("orchestrions", run.Port.State.Unlocks["orchestrions"] with { UnlockedIds = [1] }) };
        run.Controller.HintSource("orchestrions", 123, 20);
        run.Update(20.25); run.Update(20.25);
        var deadline = Stopwatch.StartNew();
        while (run.Controller.DiagnosticTasks.Single(task => task.Name == "CollectionOutput").State is not ("Completed" or "Faulted" or "Cancelled"))
        {
            Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(5)); await Task.Delay(1);
        }
        Assert.Equal("Completed", run.Controller.DiagnosticTasks.Single(task => task.Name == "CollectionOutput").State);
        Assert.Empty(run.Controller.Publication.Current!.Data.State.Unlocks["orchestrions"].UnlockedIds);
        var changes = new List<string>(); run.Controller.ChangedV11 += changes.Add;
        run.Controller.Update(0, "", true, false, 21, SnapshotFixture.Now.AddSeconds(21));
        Assert.Equal(0ul, run.Controller.ActiveContentId);
        Assert.Null(run.Controller.Publication.Current);
        Assert.Empty(changes);
        Assert.True(run.Controller.DiagnosticOperation(21).StaleCompletionRejected);
    }
    [Fact]
    public async Task RegistrationPublishesValidatedNoticesAndOnlyObservedSourcesThenUnchangedRetryKeepsRevision()
    {
        await using var run = new Harness();
        await run.Initialize();
        var before = run.Controller.Publication.Current!;
        run.Port.State = run.Port.State with
        {
            Unlocks = run.Port.State.Unlocks.SetItem("orchestrions",
            run.Port.State.Unlocks["orchestrions"] with { UnlockedIds = [1] })
        };
        var notices = new List<string>(); run.Controller.ChangedV11 += notices.Add;
        run.Controller.HintSource("orchestrions", 123, 20);
        await run.Cycle(20.25);
        var accepted = run.Controller.Publication.Current!;
        Assert.Equal(before.Revision + 1, accepted.Revision);
        Assert.Equal(new uint[] { 1 }, accepted.Data.State.Unlocks["orchestrions"].UnlockedIds);
        Assert.Equal(before.Data.State.CurrentItemPresence["inventory"].Health, accepted.Data.State.CurrentItemPresence["inventory"].Health);
        Assert.True(Exportaramus.ConsumerKit.V1_1.CollectionDataSemanticValidator.ValidateSnapshot(accepted.CompactJsonV11, "123").Success);
        var notice = Assert.Single(notices);
        Assert.True(Exportaramus.ConsumerKit.V1_1.CollectionDataSemanticValidator.ValidateChanged(notice).Success);
        using var json = JsonDocument.Parse(notice);
        var sources = json.RootElement.GetProperty("changedSources").EnumerateArray().Select(value => value.GetString()).ToArray();
        Assert.Contains("orchestrions", sources); Assert.DoesNotContain("inventory", sources);
        await run.Cycle(21.25);
        Assert.Same(accepted, run.Controller.Publication.Current);
        Assert.Single(notices);
    }

    [Fact]
    public async Task AHintBeforeRegistrationIsNotProofAndItsDelayedRetryFindsTheNewState()
    {
        await using var run = new Harness(); await run.Initialize();
        var before = run.Controller.Publication.Current!;
        run.Controller.HintSource("orchestrions", 123, 20);
        await run.Cycle(20.25);
        Assert.Same(before, run.Controller.Publication.Current);
        Assert.Empty(before.Data.State.Unlocks["orchestrions"].UnlockedIds);
        run.Port.State = run.Port.State with
        {
            Unlocks = run.Port.State.Unlocks.SetItem("orchestrions",
            run.Port.State.Unlocks["orchestrions"] with { UnlockedIds = [1] })
        };
        await run.Cycle(21.25);
        Assert.Equal(new uint[] { 1 }, run.Controller.Publication.Current!.Data.State.Unlocks["orchestrions"].UnlockedIds);
        Assert.Equal(before.Revision + 1, run.Controller.Publication.Current.Revision);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string path = Path.Combine(AppContext.BaseDirectory, "prompt-publication", Guid.NewGuid().ToString("N"));
        public Port Port { get; } = new();
        public SnapshotController Controller { get; }
        public Harness() => Controller = new(Port, new(path), null);
        public void Update(double seconds) => Controller.Update(123, "Fixture", false, false, seconds, SnapshotFixture.Now.AddSeconds(seconds));
        public async Task Initialize()
        {
            var deadline = Stopwatch.StartNew();
            while (!Controller.Reports.Available || Controller.Busy)
            {
                Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(5), Controller.Error);
                Update(0); await Task.Delay(1);
            }
            Controller.RequestFull(); await Cycle(5);
            Assert.NotNull(Controller.Publication.Current);
        }
        public async Task Cycle(double seconds)
        {
            Update(seconds);
            var deadline = Stopwatch.StartNew();
            while (Controller.Busy)
            {
                Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(5), Controller.Error);
                await Task.Delay(1); Update(seconds);
            }
            Assert.Null(Controller.Error);
        }
        public async ValueTask DisposeAsync()
        {
            Controller.Dispose(); await Controller.ShutdownTask.WaitAsync(TimeSpan.FromSeconds(5));
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    private sealed class Port : ISnapshotCapturePort
    {
        public CollectionState State = SnapshotFixture.Create().State;
        private RawCaptureBatch? result;
        public List<(bool Full, double Time)> Scheduled { get; } = [];
        public bool FailNextRead;
        public bool TargetedSupported = true;
        public Port() => State = State with { Unlocks = State.Unlocks.SetItem("orchestrions", State.Unlocks["orchestrions"] with { UnlockedIds = [] }) };
        public CollectionCatalog? Catalog => SnapshotFixture.Create().Catalog;
        public bool SupportsTargetedCapture => TargetedSupported;
        public bool PrepareCatalog() => true;
        public void Begin(bool full, ulong contentId, long generation, DateTimeOffset now)
        {
            Scheduled.Add((full, (now - SnapshotFixture.Now).TotalSeconds));
            result = new(full ? State.Unlocks : ImmutableDictionary<string, PermanentUnlockState>.Empty, State.CurrentItemPresence, State.Quests);
        }
        public void BeginTargeted(ImmutableArray<string> sources, ulong contentId, long generation, DateTimeOffset now) =>
            result = new(State.Unlocks.Where(pair => sources.Contains(pair.Key)).ToImmutableDictionary(),
                State.CurrentItemPresence.Where(pair => sources.Contains(pair.Key)).ToImmutableDictionary(), sources.Contains("quests") ? State.Quests : []);
        public bool Advance()
        {
            if (FailNextRead) { FailNextRead = false; throw new IOException("Transient capture failure."); }
            return true;
        }
        public RawCaptureBatch? TakeResult() => result;
        public void ResetSession() => result = null;
        public void Dispose() { }
    }
}
