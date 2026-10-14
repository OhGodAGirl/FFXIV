using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class PromptCaptureRegressionTests
{
    [Fact]
    public async Task AChangedUnlockNoticeIncludesAnUnchangedRetainedPresenceSourceThatWasAlsoRead()
    {
        await using var run = new Scenario();
        run.Port.State = run.Port.State with { Unlocks = run.Port.State.Unlocks.SetItem("orchestrions", run.Port.State.Unlocks["orchestrions"] with { UnlockedIds = [] }) };
        await run.Initialize();
        run.Port.FailInventory = true;
        run.Controller.HintSource("inventory", 123, 20); run.Update(20.25);
        await run.Pump(20.25, () => !run.Controller.Busy);
        var retained = run.Controller.Publication.Current!;
        Assert.Equal(SourceStatus.Stale, retained.Data.State.CurrentItemPresence["inventory"].Health.SourceStatus);
        var notices = new List<string>(); run.Controller.ChangedV11 += notices.Add;
        run.Port.State = run.Port.State with { Unlocks = run.Port.State.Unlocks.SetItem("orchestrions", run.Port.State.Unlocks["orchestrions"] with { UnlockedIds = [1] }) };
        run.Controller.HintSource("orchestrions", 123, 21.5); run.Controller.HintSource("inventory", 123, 21.5);
        run.Update(21.75); await run.Pump(21.75, () => !run.Controller.Busy);
        var accepted = run.Controller.Publication.Current!;
        Assert.Equal(retained.SourceFingerprints["inventory"], accepted.SourceFingerprints["inventory"]);
        Assert.Equal(new uint[] { 1 }, accepted.Data.State.Unlocks["orchestrions"].UnlockedIds);
        using var notice = JsonDocument.Parse(Assert.Single(notices));
        var sources = notice.RootElement.GetProperty("changedSources").EnumerateArray().Select(value => value.GetString()).ToArray();
        Assert.Contains("orchestrions", sources);
        Assert.Contains("inventory", sources);
    }

    [Fact]
    public async Task EmptyRestorationDoesNotTreatAnEarlierCaptureFailureAsASuccessfulCapture()
    {
        await using var run = new Scenario();
        await run.Initialize();
        run.Port.FailNextRead = true;
        run.Controller.RequestFull(); run.Update(10); run.Update(10);
        var failure = run.Controller.Error;
        Assert.False(string.IsNullOrEmpty(failure));
        run.Update(20, 456);
        await run.Pump(20, () => run.Controller.Reports.Available && !run.Controller.Busy, 456);
        Assert.Null(run.Controller.Publication.Current);
        Assert.Equal(failure, run.Controller.Error);
        run.Controller.RequestFull(); run.Update(21, 456);
        await run.Pump(21, () => run.Controller.Publication.Current is not null && !run.Controller.Busy, 456);
        Assert.Equal(456ul, run.Controller.Publication.Current!.Data.State.ContentId);
        Assert.Null(run.Controller.Error);
    }

    [Fact]
    public void ADelayedHintIsEligibleAtItsDeadlineButNotAfterTheRetryWindow()
    {
        var atDeadline = new PromptCapturePolicy();
        atDeadline.SetCharacter(123, 0); atDeadline.Take(15);
        atDeadline.Hint("orchestrions", 123, 20);
        Assert.Contains("orchestrions", atDeadline.Take(35));
        var afterDeadline = new PromptCapturePolicy();
        afterDeadline.SetCharacter(123, 0); afterDeadline.Take(15);
        afterDeadline.Hint("orchestrions", 123, 20);
        Assert.DoesNotContain("orchestrions", afterDeadline.Take(35.001));
    }

    [Fact]
    public async Task NoOpTargetedCompletionPreservesPublishedCapabilities()
    {
        await using var run = new Scenario();
        await run.Initialize();
        var capabilities = run.Controller.Publication.GetCapabilities();
        Assert.False(string.IsNullOrEmpty(capabilities));
        var accepted = run.Controller.Publication.Current;
        run.Controller.HintSource("orchestrions", 123, 20);
        run.Update(20.25);
        await run.Pump(20.25, () => !run.Controller.Busy);
        Assert.Same(accepted, run.Controller.Publication.Current);
        Assert.Equal(capabilities, run.Controller.Publication.GetCapabilities());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureCompletionDoesNotDiscardReportsAcceptedWhileItsWorkerWasPending(bool targeted)
    {
        await using var run = new Scenario();
        await run.Initialize();
        if (targeted) run.Controller.HintSource("orchestrions", 123, 20);
        else run.Controller.RequestFull();
        run.Update(20.25); // Start the native read.
        run.Update(20.25); // Finish the read and start its worker; acceptance requires a later update.
        Assert.True(run.Controller.Busy);
        Assert.Equal("Accepted", run.Controller.Reports.TryEnqueue("123", 100, "Pending fixture", 20.25, SnapshotFixture.Now.AddSeconds(20.25)));
        await run.Pump(20.25, () => run.Controller.Publication.Current!.Data.State.ReportedEvidence.Items.Any(
            item => item.ItemId == 100 && item.Reporter == "Pending fixture") && !run.Controller.Busy);
        Assert.Null(run.Controller.Error);
    }

    [Fact]
    public void DelayedRegistrationRetriesDoNotRunBeforeTheirScheduledDelay()
    {
        var policy = new PromptCapturePolicy();
        policy.SetCharacter(123, 0); policy.Take(15);
        policy.Hint("orchestrions", 123, 20);
        Assert.Contains("orchestrions", policy.Take(20.25));
        Assert.Contains("orchestrions", policy.Take(21.25));
        Assert.DoesNotContain("orchestrions", policy.Take(24.249));
        Assert.Contains("orchestrions", policy.Take(24.25));
        Assert.DoesNotContain("orchestrions", policy.Take(32.249));
        Assert.Contains("orchestrions", policy.Take(32.25));
    }

    [Fact]
    public void QuestFallbackPreservesAnUnrelatedHintDueInTheSameFrame()
    {
        var policy = new PromptCapturePolicy();
        policy.SetCharacter(123, 0); policy.Take(15);
        policy.Hint("orchestrions", 123, 299.75);
        var sources = policy.Take(300);
        Assert.Contains("quests", sources);
        Assert.Contains("orchestrions", sources);
        Assert.Equal(sources.Length, sources.Distinct().Count());
    }

    [Fact]
    public void UnchangedQuestRefreshDoesNotPublishAnotherRevision()
    {
        var prior = SnapshotFixture.Create().State;
        var raw = new RawCaptureBatch(ImmutableDictionary<string, PermanentUnlockState>.Empty.Add("quests", prior.Unlocks["quests"]),
            ImmutableDictionary<string, CurrentItemPresenceState>.Empty, prior.Quests);
        var refreshed = TargetedCaptureMerge.Apply(prior, raw, prior.CaptureGeneration + 1, SnapshotFixture.Now.AddSeconds(1));
        Assert.False(TargetedCaptureChangePolicy.HasChanges(prior, refreshed, raw));
    }

    [Fact]
    public void FailedPresenceRefreshRetainsItemsAndTheirOriginalObservationTime()
    {
        var prior = SnapshotFixture.Create().State;
        var held = prior.CurrentItemPresence["inventory"];
        Assert.NotEmpty(held.Items);
        var failed = held with { Items = [], Health = CaptureReadPolicy.Health(DataType.CurrentItemPresence, 2, SnapshotFixture.Now.AddSeconds(2), ReasonCode.CaptureFailed, true) };
        var retained = TargetedCaptureMerge.Apply(prior, Presence(failed), 2, SnapshotFixture.Now.AddSeconds(2));
        Assert.Equal(held.Items, retained.CurrentItemPresence["inventory"].Items);
        Assert.Equal(SourceStatus.Stale, retained.CurrentItemPresence["inventory"].Health.SourceStatus);
        Assert.Equal(held.Health.ObservedAtUtc, retained.CurrentItemPresence["inventory"].Health.ObservedAtUtc);
        Assert.Equal(ReasonCode.CaptureFailed, retained.CurrentItemPresence["inventory"].Health.ReasonCode);
        Assert.True(TargetedCaptureChangePolicy.HasChanges(prior, retained, Presence(failed)));
    }

    private static RawCaptureBatch Presence(CurrentItemPresenceState state) => new(
        ImmutableDictionary<string, PermanentUnlockState>.Empty,
        ImmutableDictionary<string, CurrentItemPresenceState>.Empty.Add("inventory", state), []);

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly string path = Path.Combine(AppContext.BaseDirectory, "prompt-regression", Guid.NewGuid().ToString("N"));
        public SnapshotController Controller { get; }
        public Port Port { get; } = new();
        public Scenario() => Controller = new(Port, new(path), null);
        public void Update(double seconds, ulong contentId = 123) => Controller.Update(contentId, "Fixture", false, false, seconds, SnapshotFixture.Now.AddSeconds(seconds));
        public async Task Initialize()
        {
            await Pump(0, () => Controller.Reports.Available && !Controller.Busy);
            Controller.RequestFull(); Update(5);
            await Pump(5, () => Controller.Publication.Current is not null && !Controller.Busy);
        }
        public async Task Pump(double seconds, Func<bool> finished, ulong contentId = 123)
        {
            var deadline = Stopwatch.StartNew();
            while (!finished())
            {
                Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(5), Controller.Error);
                Update(seconds, contentId); await Task.Delay(1);
            }
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
        public bool FailNextRead;
        public bool FailInventory;
        public CollectionCatalog? Catalog => SnapshotFixture.Create().Catalog;
        public bool SupportsTargetedCapture => true;
        public bool PrepareCatalog() => true;
        public void Begin(bool full, ulong contentId, long generation, DateTimeOffset now) => result = new(
            full ? State.Unlocks : ImmutableDictionary<string, PermanentUnlockState>.Empty, State.CurrentItemPresence, State.Quests);
        public void BeginTargeted(ImmutableArray<string> sources, ulong contentId, long generation, DateTimeOffset now) => result = new(
            State.Unlocks.Where(pair => sources.Contains(pair.Key)).ToImmutableDictionary(),
            State.CurrentItemPresence.Where(pair => sources.Contains(pair.Key)).ToImmutableDictionary(pair => pair.Key,
                pair => pair.Key == "inventory" && FailInventory ? CaptureReadPolicy.FailedPresence(true, generation, now, ReasonCode.CaptureFailed) : pair.Value), sources.Contains("quests") ? State.Quests : []);
        public bool Advance()
        {
            if (FailNextRead) { FailNextRead = false; throw new IOException("Controlled capture failure."); }
            return true;
        }
        public RawCaptureBatch? TakeResult() => result;
        public void ResetSession() => result = null;
        public void Dispose() { }
    }
}
