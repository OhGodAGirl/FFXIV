using System.Collections.Immutable;
using System.Text.Json;
using Exportaramus.ConsumerKit.V1;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Diagnostics;
using Exportaramus.Features.Snapshots;
using Harness = Exportaramus.Tests.Snapshots.ControllerSchedulingIntegrationTests.Harness;

namespace Exportaramus.Tests.Snapshots;

public sealed class ControllerMutationContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithdrawalUsesTheActualConsumerContractAndDepartingIdentity(bool stopping)
    {
        await using var run = new Harness();
        await run.Restore(0);
        var notices = new List<string>();
        run.Controller.Withdrawn += notices.Add;
        var before = DateTimeOffset.UtcNow;
        if (stopping) run.Controller.Dispose();
        else run.Controller.Update(0, "", true, false, 10, SnapshotFixture.Now.AddSeconds(10));
        var after = DateTimeOffset.UtcNow;
        var payload = Assert.Single(notices);
        Assert.True(CollectionDataSemanticValidator.ValidateWithdrawn(payload).Success);
        using var document = JsonDocument.Parse(payload);
        var value = document.RootElement;
        Assert.Equal("SnapshotWithdrawn", value.GetProperty("eventType").GetString());
        Assert.Equal("1.0", value.GetProperty("schemaVersion").GetString());
        Assert.Equal(run.Controller.Session, value.GetProperty("providerSessionId").GetGuid());
        Assert.Equal("123", value.GetProperty("contentId").GetString());
        Assert.Equal(stopping ? "ProviderStopping" : "LoggedOut", value.GetProperty("reasonCode").GetString());
        var time = value.GetProperty("withdrawnAtUtc").GetDateTimeOffset();
        if (stopping) Assert.InRange(time, before, after);
        else Assert.Equal(SnapshotFixture.Now.AddSeconds(10), time);
    }

    [Fact]
    public async Task RequestsOutsideActiveLifetimeLeaveThePublicPendingRequestEmpty()
    {
        await using var run = new Harness();
        run.Controller.RequestFull();
        Assert.Equal("None", run.Controller.DiagnosticOperation(0).PendingRequestPriority);
        run.Controller.RequestPresence();
        Assert.Equal("None", run.Controller.DiagnosticOperation(0).PendingRequestPriority);
        await run.Restore(0);
        run.Controller.Dispose();
        run.Controller.RequestFull();
        Assert.Equal("None", run.Controller.DiagnosticOperation(0).PendingRequestPriority);
        run.Controller.RequestPresence();
        Assert.Equal("None", run.Controller.DiagnosticOperation(0).PendingRequestPriority);
        Assert.Equal("ProviderStopping", run.Controller.Reports.TryEnqueue("123", 100, "fixture", 1, SnapshotFixture.Now));
    }

    [Fact]
    public async Task AcceptedFullAndReportPublishIncreasingGenerationsAndSortedEvidence()
    {
        await using var run = new Harness();
        await run.Restore(0);
        run.Controller.RequestFull(); await run.Accept(5, 1, CaptureType.Full);
        var fullGeneration = run.Controller.Publication.Current!.Data.State.CaptureGeneration;
        Assert.True(fullGeneration > 0);
        Assert.Equal(fullGeneration, run.Controller.DiagnosticOperation(5).CaptureGeneration);
        Assert.Equal("Accepted", run.Controller.Reports.TryEnqueue("123", 1675, "Zulu", 10, SnapshotFixture.Now));
        Assert.Equal("Accepted", run.Controller.Reports.TryEnqueue("123", 100, "Zulu", 10, SnapshotFixture.Now));
        Assert.Equal("Accepted", run.Controller.Reports.TryEnqueue("123", 100, "Alpha", 10, SnapshotFixture.Now));
        await run.Accept(10, 2, CaptureType.Reported);
        var state = run.Controller.Publication.Current!.Data.State;
        Assert.True(state.CaptureGeneration > fullGeneration);
        Assert.Equal(state.CaptureGeneration, run.Controller.DiagnosticOperation(10).CaptureGeneration);
        Assert.Equal(new[] { (100u, "Alpha"), (100u, "Zulu"), (1675u, "Zulu") },
            state.ReportedEvidence.Items.Select(item => (item.ItemId, item.Reporter)));
    }

    [Fact]
    public async Task FailedSameCharacterRefreshRetainsPhysicalPresenceButClearsPermanentUnlocks()
    {
        await using var run = new Harness();
        await run.Restore(0);
        var fixture = SnapshotFixture.Create().State;
        run.Port.SuppliedBatch = new(fixture.Unlocks, fixture.CurrentItemPresence, fixture.Quests);
        run.Controller.RequestFull(); await run.Accept(5, 1, CaptureType.Full);
        var previous = run.Controller.Publication.Current!.Data.State;
        run.Port.SuppliedBatch = new(
            fixture.Unlocks.ToImmutableDictionary(pair => pair.Key, pair => pair.Value with
            { Health = pair.Value.Health with { SourceStatus = SourceStatus.Unavailable, Completeness = Completeness.Unknown, ProvenanceType = ProvenanceType.None, ObservedAtUtc = null, ReasonCode = ReasonCode.CaptureFailed } }),
            fixture.CurrentItemPresence.ToImmutableDictionary(pair => pair.Key, pair => pair.Value with
            { Items = [], Health = pair.Value.Health with { SourceStatus = SourceStatus.Unavailable, Completeness = Completeness.Unknown, ProvenanceType = ProvenanceType.None, ObservedAtUtc = null, ReasonCode = ReasonCode.CaptureFailed } }), []);
        run.Controller.RequestFull(); await run.Accept(10, 2, CaptureType.Full);
        var updated = run.Controller.Publication.Current!.Data.State;
        Assert.All(updated.Unlocks.Values, source => Assert.Empty(source.UnlockedIds));
        foreach (var (key, source) in updated.CurrentItemPresence)
        {
            Assert.Equal(previous.CurrentItemPresence[key].Items, source.Items);
            Assert.Equal(SourceStatus.Stale, source.Health.SourceStatus);
            Assert.Equal(ReasonCode.CaptureFailed, source.Health.ReasonCode);
        }
    }

    [Fact]
    public async Task OrdinaryCaptureFailureReportsSynchronizationFailureAndIdleTime()
    {
        var measurements = new DiagnosticMeasurements();
        await using var run = new Harness(measurements);
        await run.Restore(0);
        run.Controller.RequestFull(); await run.Accept(5, 1, CaptureType.Full);
        Assert.True(measurements.Snapshot().ContainsKey("Normalization"));
        Assert.Equal(0, run.Controller.DiagnosticOperation(100).ElapsedMilliseconds);
        Assert.Equal(new[] { "CollectionOutput", "Capabilities", "CaptureShutdown", "ControllerShutdown" },
            run.Controller.DiagnosticTasks.Select(task => task.Name));
        run.Port.FullFailures = 1;
        run.Controller.RequestFull(); run.Update(101);
        Assert.False(run.Controller.Busy);
        Assert.Equal("Synchronization failed. Try Sync Now again.", run.Controller.Error);
        var failure = Assert.Single(measurements.Failures, entry => entry.Component == "Controller");
        Assert.Equal("Update", failure.Operation);
        Assert.Equal("SynchronizationFailed", failure.Code);
        Assert.Equal(nameof(IOException), failure.ExceptionType);
        Assert.True(run.Controller.Reports.Available);
    }

    [Fact]
    public void LoginResetsPriorCadencePendingRequestsAndAutomaticRetry()
    {
        var policy = new SnapshotSchedulePolicy();
        policy.Login(0); policy.Tick(15);
        Assert.Equal(CaptureRequest.AutomaticFull, policy.Take());
        policy.Failed(CaptureRequest.AutomaticFull, 15, false);
        policy.Request(CaptureRequest.AutomaticLightweight);
        policy.Login(100);
        Assert.Equal(CaptureRequest.None, policy.Take());
        Assert.Equal(0, policy.FailureCount);
        policy.Tick(100); Assert.Equal(CaptureRequest.None, policy.Take());
        policy.Tick(114.999); Assert.Equal(CaptureRequest.None, policy.Take());
        policy.Tick(115); Assert.Equal(CaptureRequest.AutomaticFull, policy.Take());
    }

    [Fact]
    public void ManualRequestCancelsAutomaticRetryAndManualFailureNeverSchedulesRetry()
    {
        var policy = new SnapshotSchedulePolicy();
        policy.Login(0); policy.Accepted(true, 5);
        policy.Failed(CaptureRequest.AutomaticLightweight, 100, false);
        policy.Request(CaptureRequest.ManualFull);
        Assert.Equal(CaptureRequest.ManualFull, policy.Take());
        Assert.Equal(0, policy.FailureCount);
        policy.Failed(CaptureRequest.ManualFull, 110, false);
        policy.Tick(130); Assert.Equal(CaptureRequest.None, policy.Take());
        policy.Tick(140); Assert.Equal(CaptureRequest.None, policy.Take());
        policy.Tick(305); Assert.Equal(CaptureRequest.AutomaticLightweight, policy.Take());
    }

    [Fact]
    public void ResetCancelsCadenceAndPendingRetryUntilNextLogin()
    {
        var policy = new SnapshotSchedulePolicy();
        policy.Login(0); policy.Tick(15); policy.Failed(CaptureRequest.AutomaticFull, 15, false);
        policy.Reset();
        Assert.Equal(0, policy.FailureCount);
        Assert.Equal(CaptureRequest.None, policy.Take());
        policy.Tick(100000);
        Assert.Equal(CaptureRequest.None, policy.Take());
    }

    [Fact]
    public void EarlyAutomaticFullFailureDoesNotPostponePreviouslyScheduledFullCadence()
    {
        var policy = new SnapshotSchedulePolicy();
        policy.Login(0); policy.Accepted(true, 5);
        policy.Failed(CaptureRequest.AutomaticFull, 100, false);
        policy.Accepted(false, 1700);
        policy.Tick(1804.999); Assert.Equal(CaptureRequest.None, policy.Take());
        policy.Tick(1805); Assert.Equal(CaptureRequest.AutomaticFull, policy.Take());
    }

    [Fact]
    public void ResetAndReadFailureStillDetectFirstReadyZeroFingerprint()
    {
        var edge = new StorageWindowEdgePolicy();
        edge.Reset();
        Assert.True(edge.Observe(true, 0, 0, false));
        var policy = new StorageReadinessPolicy();
        Assert.False(policy.Poll(true, key => key == "glamourDresser", _ => throw new IOException(), 0, false));
        Assert.True(policy.Poll(true, key => key == "glamourDresser", _ => (true, 0ul), 1, false));
    }
}


