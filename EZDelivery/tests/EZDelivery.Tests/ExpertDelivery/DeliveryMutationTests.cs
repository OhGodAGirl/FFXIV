using EZDelivery.Features.ExpertDelivery;

namespace EZDelivery.Tests.ExpertDelivery;

public sealed partial class DeliverySafetyTests
{
    [Fact]
    public void EveryDeliveryReasonHasUsablePlayerCopy()
    {
        foreach (var reason in Enum.GetValues<DeliveryReason>())
            Assert.False(string.IsNullOrWhiteSpace(DeliveryRules.Message(reason)));
    }

    [Fact]
    public void UnavailableObservationContainsNoActionableNativeIdentity()
    {
        var value = DeliveryObservation.Unavailable(DeliveryReason.NoCharacter);
        Assert.Equal(DeliveryReadState.NotReady, value.State); Assert.Equal(DeliveryReason.NoCharacter, value.Reason);
        Assert.Null(value.Context); Assert.Null(value.Anchor); Assert.Equal(-1, value.Filter); Assert.Equal(-1, value.Sort);
        Assert.Equal(0, value.MapGeneration); Assert.Equal(0u, value.Seals); Assert.Equal(0u, value.SealCap);
        Assert.Empty(value.Rows); Assert.Null(value.PendingSource); Assert.False(value.PendingSourceReadable);
        Assert.Equal(new DeliveryDialog(DeliveryDialogKind.None, 0, 0, false, null, false), value.Dialog);
        Assert.True(DeliveryDispatchResult.Sent.Dispatched); Assert.True(DeliveryDispatchResult.Sent.MayHaveDispatched);
        Assert.Equal(DeliveryReason.None, DeliveryDispatchResult.Sent.Reason);
    }

    [Fact]
    public void ExactMaximumNativeListRemainsSelectableAndReportsMapping()
    {
        var rows = Enumerable.Range(0, 2048).Select(index => Row with { VisibleIndex = index, AgentIndex = index + 11, Source = Source with { Slot = index } }).ToArray();
        Assert.True(DeliveryRules.ValidMapping(rows));
        var records = Enumerable.Repeat(new DeliverySelectionRecord(0, false, 1, 0, false), 11)
            .Concat(Enumerable.Repeat(new DeliverySelectionRecord(2, false, 1, 0, false), 2048)).ToArray();
        Assert.True(DeliverySelectionMapping.TryResolve(records, Enumerable.Range(0, records.Length).ToArray(), 0, 2048, out var selected, out var check));
        Assert.Equal(2048, selected.Count); Assert.False(string.IsNullOrWhiteSpace(check));
        Assert.False(DeliverySelectionMapping.TryResolve(records, Enumerable.Range(0, records.Length).ToArray(), 3, 2048, out _, out check));
        Assert.False(string.IsNullOrWhiteSpace(check));
        var order = Enumerable.Range(0, records.Length).ToArray(); order[0] = -1;
        Assert.False(DeliverySelectionMapping.TryResolve(records, order, 0, 2048, out _, out check));
        Assert.False(string.IsNullOrWhiteSpace(check));
        Assert.False(DeliverySelectionMapping.TryResolve(records, Enumerable.Range(0, records.Length).ToArray(), 0, 1, out _, out check));
        Assert.False(string.IsNullOrWhiteSpace(check));
    }

    [Fact]
    public void RunningToggleAloneStopsTheRun()
    {
        var (native, engine) = Start(); engine.Toggle(2); engine.Tick(3);
        Assert.Equal(DeliveryPhase.Stopped, engine.Status.Phase); AssertReason(DeliveryReason.Cancelled, engine); Assert.Single(native.Calls);
        engine.Stop(); Assert.Equal(DeliveryPhase.Stopped, engine.Status.Phase);
    }

    [Fact]
    public void StopWhileIdleRecordsCancellation()
    {
        var engine = new ExpertDeliveryFacade(new Fake()); engine.Stop();
        Assert.Equal(DeliveryPhase.Stopped, engine.Status.Phase); AssertReason(DeliveryReason.Cancelled, engine);
    }

    [Fact]
    public void ExpiredPostReceiptWaitCannotSelectFromAReadyList()
    {
        var (native, engine) = Start(); Reward(native); engine.Tick(2);
        native.Value = native.Value with { PendingSource = null, Seals = 45000 }; engine.Tick(3);
        native.Value = Ready(); engine.Tick(5003);
        Assert.Equal(DeliveryPhase.Stopped, engine.Status.Phase); AssertReason(DeliveryReason.NotReady, engine);
        Assert.Equal(2, native.Calls.Count);
    }

    [Fact]
    public void ContextFailureReasonIsNotOverwrittenByLaterPendingChecks()
    {
        var (native, engine) = Start(); Reward(native);
        native.Value = native.Value with { Context = Context with { Addon = 99 } }; engine.Tick(3);
        AssertReason(DeliveryReason.WrongContext, engine); Assert.Single(native.Calls);
    }

    [Fact]
    public void UnrelatedDialogIsReportedEvenWhenThePendingDeadlineAlsoExpired()
    {
        var (native, engine) = Start();
        native.Value = native.Value with { Dialog = new(DeliveryDialogKind.Unrelated, 9, Source.ItemId, false, null, true) };
        engine.Tick(5001); AssertReason(DeliveryReason.UnrelatedDialog, engine); Assert.Single(native.Calls);
    }

    [Fact]
    public void DeliveryProgressAndPendingLedgerReflectDispatchAndReceiptStages()
    {
        var (native, engine) = Start();
        Assert.False(native.Calls[0].Pending.Submitted);
        native.Value = native.Value with { Dialog = new(DeliveryDialogKind.HqWarning, 7, Source.ItemId, true, null, true) };
        engine.Tick(2);
        Assert.Equal(DeliveryPhase.WaitReward, engine.Status.Phase); Assert.Equal("Waiting for the delivery reward", engine.Status.Message);
        Reward(native); engine.Tick(3);
        Assert.Equal(1, engine.Status.DispatchedCount); Assert.True(native.Calls[^1].Pending.Submitted);
        Assert.Equal("Checking the delivery result", engine.Status.Message);
        engine.Tick(5003);
        Assert.Equal(DeliveryPhase.OutcomeUncertain, engine.Status.Phase);
        engine.Tick(5004); Assert.NotNull(native.Observed[^1]); Assert.True(native.Observed[^1]!.Submitted);

        (native, engine) = Start(); Reward(native); native.Result = DeliveryDispatchResult.Rejected(DeliveryReason.RewardUnknown); engine.Tick(2);
        AssertReason(DeliveryReason.RewardUnknown, engine); Assert.Equal(DeliveryPhase.Stopped, engine.Status.Phase);
        engine.Tick(3); Assert.Null(native.Observed[^1]);
    }

    [Fact]
    public void ZeroFinalRewardStopsAsUnknownButZeroEstimateDoesNotAuthorizeSubmission()
    {
        var (native, engine) = Start(); Reward(native, 0); engine.Tick(2);
        AssertReason(DeliveryReason.RewardUnknown, engine);
        native = new Fake { Value = Ready(Row with { Reward = 0 }) with { Seals = 45000 } };
        engine = new ExpertDeliveryFacade(native); engine.Toggle(3); engine.Tick(4);
        Assert.Equal(DeliveryCommandKind.Select, Assert.Single(native.Calls).Kind);
    }

    [Fact]
    public void PossibleSubmitKeepsFullReceiptDeadlineAndCannotFinishEarly()
    {
        var (native, engine) = Start(); Reward(native); native.Result = new(false, true, DeliveryReason.DispatchRejected);
        engine.Tick(10); engine.Tick(11);
        Assert.Equal(DeliveryPhase.Reconcile, engine.Status.Phase);
        native.Value = native.Value with { PendingSource = null, Seals = 45000 }; engine.Tick(12);
        Assert.Equal(DeliveryPhase.Cooldown, engine.Status.Phase); Assert.Equal("Delivery confirmed; waiting for the item list", engine.Status.Message);
        native.Value = Ready() with { State = DeliveryReadState.Inconsistent, Reason = DeliveryReason.MappingMismatch };
        engine.Tick(592); Assert.Equal("Refreshing the Expert Delivery list", engine.Status.Message);
    }

    [Fact]
    public void StopOrSessionLossAfterReceiptMustRetainStoppedPhase()
    {
        var (native, engine) = Start(); Reward(native); engine.Tick(2); engine.Stop();
        native.Value = native.Value with { PendingSource = null, Seals = 45000 }; engine.Tick(3);
        Assert.Equal(DeliveryPhase.Stopped, engine.Status.Phase);
        (native, engine) = Start(); Reward(native); engine.Tick(2);
        native.Value = native.Value with { Context = Context with { Territory = 999 }, PendingSource = null, Seals = 45000 }; engine.Tick(3);
        Assert.Equal(DeliveryPhase.Stopped, engine.Status.Phase);
        (native, engine) = Start(); Reward(native); engine.Tick(2);
        native.Value = native.Value with { PendingSource = null, Seals = 45000 }; engine.Tick(3);
        native.Value = Ready() with { Context = null }; engine.Tick(600);
        Assert.Equal(DeliveryPhase.Stopped, engine.Status.Phase); AssertReason(DeliveryReason.WrongContext, engine);
    }
}
