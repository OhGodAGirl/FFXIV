using EZDelivery.Features.ExpertDelivery;
using Xunit;

namespace EZDelivery.Tests.ExpertDelivery;

public sealed partial class DeliverySafetyTests
{
    [Theory]
    [InlineData(true, true, 10, 0, false, true)]
    [InlineData(true, false, 10, 10, true, true)]
    [InlineData(true, false, 10, 0, false, false)]
    [InlineData(true, false, 11, 10, true, false)]
    [InlineData(true, false, 10, 10, false, false)]
    [InlineData(false, false, 10, 10, true, false)]
    [InlineData(false, false, 0, 10, true, false)]
    public void HiddenListIsRetainedOnlyForItsPendingOperation(bool ready, bool visible, long addon, long pending, bool generation, bool expected)
        => Assert.Equal(expected, DeliveryRules.CanObserveListContext(ready, visible, addon, pending, generation));

    [Fact]
    public void OwnedRewardWithHiddenListCanSubmitAndStillChecksSealCapacity()
    {
        var (native, engine) = Start();
        native.Value = native.Value with { Anchor = null, Rows = [], State = DeliveryReadState.NotReady };
        Reward(native); engine.Tick(10);
        Assert.Equal(DeliveryCommandKind.Submit, native.Calls[1].Kind);
        var (overflowNative, overflowEngine) = Start();
        overflowNative.Value = overflowNative.Value with { Anchor = null, Rows = [], State = DeliveryReadState.NotReady };
        Reward(overflowNative, 501); overflowEngine.Tick(10);
        Assert.Equal(DeliveryCommandKind.CancelReward, overflowNative.Calls[1].Kind);
        Assert.Equal(DeliveryReason.SealCapacity, overflowEngine.Status.Reason);
    }

    [Theory]
    [InlineData(true, true, true, true, true, DeliveryDialogKind.HqWarning)]
    [InlineData(false, true, true, true, true, DeliveryDialogKind.Unrelated)]
    [InlineData(true, false, true, true, true, DeliveryDialogKind.Unrelated)]
    [InlineData(true, true, false, true, true, DeliveryDialogKind.Unrelated)]
    [InlineData(true, true, true, false, true, DeliveryDialogKind.Unrelated)]
    [InlineData(true, true, true, true, false, DeliveryDialogKind.Unrelated)]
    public void WarningOwnershipRequiresPendingSourceCallbackPromptAndQuality(bool generation, bool row,
        bool callback, bool prompt, bool hq, DeliveryDialogKind expected)
        => Assert.Equal(expected, DeliveryRules.WarningKind(generation, row, callback, false, prompt, false, hq));

    [Fact]
    public void ReportedHqCorrelationReachesConfirmationWithoutGenericYes()
    {
        var (native, engine) = Start();
        // Recorded failure: all these predicates true, only the agent's cached ID differed.
        var kind = DeliveryRules.WarningKind(true, true, true, false, true, false, true);
        native.Value = native.Value with { Dialog = new(kind, 79, Source.ItemId, true, null, true) };
        engine.Tick(5);
        engine.Tick(6);
        Assert.Equal(new[] { DeliveryCommandKind.Select, DeliveryCommandKind.ConfirmHq }, native.Calls.Select(x => x.Kind));
        Reward(native); engine.Tick(7);
        Assert.Equal(DeliveryCommandKind.Submit, native.Calls[2].Kind);
    }

    private static readonly DeliveryContext Context = new(123, 128, 1, 9, 50);
    private static readonly DeliverySource Source = new(0, 3, 1000123, 123, true, 1, 991);
    private static readonly DeliveryRow Row = new(0, 12, Source, true, 500, "Item");
    private static void AssertReason(DeliveryReason expected, ExpertDeliveryFacade engine)
    {
        Assert.Equal(expected, engine.Status.Reason);
        Assert.False(string.IsNullOrWhiteSpace(engine.Status.Message));
    }
    private static DeliveryObservation Ready(params DeliveryRow[] rows) => new(DeliveryReadState.Ready, DeliveryReason.None, Context,
        new(20, 30, 500, 600, 1), 0, 1, 1, 44500, 45000, rows.Length == 0 ? [Row] : rows, Source, true, new(DeliveryDialogKind.None, 0, 0, false, null, false));
    private sealed class Fake : IDeliveryNativeAdapter
    {
        public DeliveryObservation Value = Ready();
        public List<DeliveryCommand> Calls { get; } = [];
        public DeliveryDispatchResult Result = DeliveryDispatchResult.Sent;
        public bool ThrowDispatch;
        public List<DeliveryRefreshRequest> Refreshes { get; } = [];
        public List<DeliveryPending?> Observed { get; } = [];
        public DeliveryDispatchResult RefreshResult = DeliveryDispatchResult.Sent;
        public bool ThrowRefresh;
        public DeliveryObservation Observe(DeliveryPending? pending) { Observed.Add(pending); return Value; }
        public DeliveryDispatchResult Dispatch(DeliveryCommand command) { Calls.Add(command); if (ThrowDispatch) throw new InvalidOperationException(); return Result; }
        public DeliveryDispatchResult RefreshExpertList(DeliveryRefreshRequest request) { Refreshes.Add(request); if (ThrowRefresh) throw new InvalidOperationException(); return RefreshResult; }
    }
    private static (Fake Native, ExpertDeliveryFacade Engine) Start()
    {
        var native = new Fake(); var engine = new ExpertDeliveryFacade(native);
        Assert.Equal("Start Expert Deliveries", engine.Status.Message);
        engine.Toggle(0);
        Assert.Equal("Reading the native item list", engine.Status.Message);
        engine.Tick(1);
        Assert.Equal("Waiting for the delivery confirmation", engine.Status.Message);
        Assert.Equal(DeliveryCommandKind.Select, Assert.Single(native.Calls).Kind);
        Assert.True(native.Calls[0].Pending.Generation > 0);
        return (native, engine);
    }
    private static void Reward(Fake native, uint amount = 500) => native.Value = native.Value with
    { Dialog = new(DeliveryDialogKind.Reward, 99, Source.ItemId, true, amount, true) };

    [Theory]
    [InlineData(44500u, 45000u, 500u, true)]
    [InlineData(44500u, 45000u, 501u, false)]
    [InlineData(uint.MaxValue - 1, uint.MaxValue, 1u, true)]
    [InlineData(uint.MaxValue, uint.MaxValue, 1u, false)]
    [InlineData(500u, 499u, 1u, false)]
    [InlineData(0u, 45000u, 0u, false)]
    public void SealPredicateUsesRoomWithoutOverflow(uint balance, uint cap, uint reward, bool expected) => Assert.Equal(expected, DeliveryRules.RewardFits(balance, cap, reward));

    [Fact] public void ExactFitDispatchesOnlyOnceAndRequiresBothDeltas()
    {
        var (native, engine) = Start(); Reward(native); engine.Tick(10);
        Assert.Equal(DeliveryCommandKind.Submit, native.Calls[1].Kind);
        native.Value = native.Value with { PendingSource = null };
        engine.Tick(20); Assert.Equal(0, engine.Status.ConfirmedCount);
        native.Value = native.Value with { Seals = 45000 };
        engine.Tick(30); Assert.Equal(1, engine.Status.ConfirmedCount);
        Assert.Equal(2, native.Calls.Count);
    }
    [Fact] public void OverflowCancelsOnlyOwnedRewardAndDoesNotSubmit()
    {
        var (native, engine) = Start(); Reward(native, 501); engine.Tick(10);
        AssertReason(DeliveryReason.SealCapacity, engine);
        Assert.Equal(DeliveryCommandKind.CancelReward, native.Calls[1].Kind);
        Assert.DoesNotContain(native.Calls, x => x.Kind == DeliveryCommandKind.Submit);
    }
    [Fact] public void BonusChangesAreRecheckedAtFinalReward()
    {
        var (native, engine) = Start(); Reward(native, 600); engine.Tick(10);
        AssertReason(DeliveryReason.SealCapacity, engine);
        Assert.DoesNotContain(native.Calls, x => x.Kind == DeliveryCommandKind.Submit);
    }
    [Fact] public void UnknownRewardCancelsWithoutSubmission()
    {
        var (native, engine) = Start(); Reward(native); native.Value = native.Value with { Dialog = native.Value.Dialog with { Reward = null } };
        engine.Tick(10); AssertReason(DeliveryReason.RewardUnknown, engine);
        Assert.DoesNotContain(native.Calls, x => x.Kind == DeliveryCommandKind.Submit);
    }
    [Fact] public void UnrelatedYesIsNeverAccepted()
    {
        var (native, engine) = Start(); native.Value = native.Value with { Dialog = new(DeliveryDialogKind.Unrelated, 88, 123, false, null, true) };
        engine.Tick(10); Assert.Single(native.Calls); AssertReason(DeliveryReason.UnrelatedDialog, engine);
    }
    [Fact] public void ShowAllHqSpiritbondSequenceDoesNotIntroduceProtection()
    {
        var (native, engine) = Start();
        native.Value = native.Value with { Dialog = new(DeliveryDialogKind.SpiritbondWarning, 78, 123, true, null, true) };
        engine.Tick(5);
        native.Value = native.Value with { Dialog = new(DeliveryDialogKind.HqWarning, 79, 123, true, null, true) };
        engine.Tick(6); Reward(native); engine.Tick(7);
        Assert.Equal(new[] { DeliveryCommandKind.Select, DeliveryCommandKind.ConfirmSpiritbond, DeliveryCommandKind.ConfirmHq, DeliveryCommandKind.Submit }, native.Calls.Select(x => x.Kind));
    }
    [Fact] public void RepeatedWarningDoesNotConfirmTwice()
    {
        var (native, engine) = Start(); native.Value = native.Value with { Dialog = new(DeliveryDialogKind.HqWarning, 79, 123, true, null, true) };
        engine.Tick(5); engine.Tick(6); Assert.Equal(2, native.Calls.Count);
        Assert.True(engine.IsRunning);
    }
    [Fact] public void StopAfterSubmissionStillReconcilesAndDoesNotSelectNext()
    {
        var (native, engine) = Start(); Reward(native); engine.Tick(10); engine.Stop();
        native.Value = native.Value with { Seals = 45000, PendingSource = null };
        engine.Tick(30); engine.Tick(1000);
        Assert.Equal(1, engine.Status.ConfirmedCount); AssertReason(DeliveryReason.Cancelled, engine); Assert.Equal(2, native.Calls.Count);
    }
    [Fact] public void OwnershipRemovalIsReportedOnlyOnceAfterBothDeliveryReceipts()
    {
        var (native, engine) = Start();
        var reports = new List<(ulong Character, uint Item)>();
        engine.Delivered += (character, item, _) => reports.Add((character, item));
        Reward(native); engine.Tick(10);
        native.Value = native.Value with { Seals = 45000 }; engine.Tick(20);
        Assert.Empty(reports);
        native.Value = native.Value with { PendingSource = null }; engine.Tick(30); engine.Tick(31);
        Assert.Single(reports);
        Assert.Equal((Context.Character, Source.ItemId), reports[0]);
    }
    [Fact] public void StopBeforeSubmissionCancelsTheOwnedReward()
    {
        var (native, engine) = Start(); Reward(native); engine.Stop(); engine.Tick(10);
        AssertReason(DeliveryReason.Cancelled, engine);
        Assert.Equal(DeliveryCommandKind.CancelReward, native.Calls[1].Kind);
    }
    [Fact] public void TimeoutNeverRetriesAndBlocksTheAffectedSourceOnNewClick()
    {
        var (native, engine) = Start(); Reward(native); engine.Tick(10); engine.Tick(5010); engine.Tick(6000);
        AssertReason(DeliveryReason.OutcomeUncertain, engine); Assert.Equal(2, native.Calls.Count);
        native.Value = Ready(); engine.Toggle(7000); engine.Tick(7001);
        AssertReason(DeliveryReason.OutcomeUncertain, engine); Assert.Equal(2, native.Calls.Count);
    }
    [Fact] public void SealsWithoutSourceRemovalCannotConfirm()
    {
        var (native, engine) = Start(); Reward(native); engine.Tick(10);
        native.Value = native.Value with { Seals = 45000 }; engine.Tick(5010);
        Assert.Equal(0, engine.Status.ConfirmedCount); AssertReason(DeliveryReason.OutcomeUncertain, engine);
    }
    [Fact] public void ClosedWindowStillAllowsPendingReceipt()
    {
        var (native, engine) = Start(); Reward(native); engine.Tick(10);
        native.Value = native.Value with { Context = Context with { Addon = 0 }, Seals = 45000, PendingSource = null };
        engine.Tick(20); Assert.Equal(1, engine.Status.ConfirmedCount); Assert.Equal(DeliveryPhase.Cooldown, engine.Status.Phase);
        engine.Tick(5020); AssertReason(DeliveryReason.NotReady, engine); Assert.Equal(2, native.Calls.Count);
    }
    [Fact] public void ConfirmedReceiptWaitsForRecreatedListThenSelectsFreshSource()
    {
        var (native, engine) = Start(); Reward(native); engine.Tick(10);
        native.Value = native.Value with { Context = Context with { Addon = 0 }, State = DeliveryReadState.NotReady, Filter = -1, Sort = -1, Seals = 45000, PendingSource = null };
        engine.Tick(20); engine.Tick(700); Assert.Equal(2, native.Calls.Count);
        native.Value = Ready(Row with { Source = Source with { Slot = 4 }, AgentIndex = 14 }) with { Context = Context with { Addon = 999 } };
        engine.Tick(800); Assert.Equal(3, native.Calls.Count);
        Assert.Equal(999, native.Calls[2].Pending.Context.Addon);
        Assert.Equal(4, native.Calls[2].Pending.Row.Source.Slot);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RecreatedListCannotBypassSessionOrFilterChange(bool session)
    {
        var (native, engine) = Start(); Reward(native); engine.Tick(10);
        native.Value = native.Value with { Seals = 45000, PendingSource = null }; engine.Tick(20);
        native.Value = Ready() with { Context = Context with { Addon = 999, Territory = session ? Context.Territory + 1 : Context.Territory }, Filter = session ? 0 : 2 };
        engine.Tick(800); Assert.Equal(2, native.Calls.Count);
        Assert.Equal(session ? DeliveryReason.WrongContext : DeliveryReason.FilterChanged, engine.Status.Reason);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PostReceiptMappingMustBecomeConsistentBeforeNextSelection(bool recovers)
    {
        var (native, engine) = Start(); Reward(native); engine.Tick(10);
        native.Value = native.Value with { Seals = 45000, PendingSource = null }; engine.Tick(20);
        native.Value = Ready() with { State = DeliveryReadState.Inconsistent, Reason = DeliveryReason.MappingMismatch };
        engine.Tick(800); Assert.Equal(2, native.Calls.Count); Assert.True(engine.IsRunning);
        if (recovers) native.Value = Ready(Row with { Source = Source with { Slot = 4 }, AgentIndex = 14 });
        engine.Tick(recovers ? 900 : 5020);
        Assert.Equal(recovers ? 3 : 2, native.Calls.Count);
        if (!recovers) AssertReason(DeliveryReason.MappingMismatch, engine);
    }
    [Fact] public void FilterChangeStopsBeforeAnyNextCall()
    {
        var (native, engine) = Start(); Reward(native); native.Value = native.Value with { Filter = 2 }; engine.Tick(10);
        Assert.Single(native.Calls); AssertReason(DeliveryReason.FilterChanged, engine);
    }
    [Fact] public void StaleListRefreshIsReceiptBoundedDelayedAndDoesNotSelectInSameTick()
    {
        var (native, engine) = Start(); Reward(native); engine.Tick(10);
        native.Value = native.Value with { Seals = 45000, PendingSource = null }; engine.Tick(20);
        native.Value = Ready() with { State = DeliveryReadState.Inconsistent, Reason = DeliveryReason.MappingMismatch };
        engine.Tick(599); Assert.Empty(native.Refreshes);
        engine.Tick(600); Assert.Single(native.Refreshes); Assert.Equal(2, native.Calls.Count);
        engine.Tick(601); Assert.Single(native.Refreshes); Assert.Equal(2, native.Calls.Count);
        native.Value = Ready(Row with { Source = Source with { Slot = 4 }, AgentIndex = 14 });
        engine.Tick(602); Assert.Equal(3, native.Calls.Count);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RefreshDoesNotCrossFilterChangesOrAnOpenDialog(bool filterChanged)
    {
        var (native, engine) = Start(); Reward(native); engine.Tick(10);
        native.Value = native.Value with { Seals = 45000, PendingSource = null }; engine.Tick(20);
        native.Value = Ready() with { State = DeliveryReadState.Inconsistent, Reason = DeliveryReason.MappingMismatch,
            Filter = filterChanged ? 2 : 0,
            Dialog = new(filterChanged ? DeliveryDialogKind.None : DeliveryDialogKind.Unrelated, 1, 0, false, null, false) };
        engine.Tick(600); Assert.Empty(native.Refreshes); Assert.Equal(2, native.Calls.Count);
        if (filterChanged) AssertReason(DeliveryReason.FilterChanged, engine);
    }
    [Fact] public void AmbiguousRefreshIsNeverRetriedAndPersistentMismatchCannotBecomeEmpty()
    {
        var (native, engine) = Start(); Reward(native); engine.Tick(10);
        native.Value = native.Value with { Seals = 45000, PendingSource = null }; engine.Tick(20);
        native.Value = Ready() with { State = DeliveryReadState.Inconsistent, Reason = DeliveryReason.MappingMismatch };
        native.RefreshResult = new(false, true, DeliveryReason.DispatchRejected);
        engine.Tick(600); engine.Tick(700); engine.Tick(5020);
        Assert.Single(native.Refreshes); AssertReason(DeliveryReason.MappingMismatch, engine);
        Assert.Equal(1, engine.Status.ConfirmedCount); Assert.Equal(2, native.Calls.Count);
    }
    [Fact] public void InitialMappingFailureNeverRequestsPostReceiptRefresh()
    {
        var native = new Fake { Value = Ready() with { State = DeliveryReadState.Inconsistent, Reason = DeliveryReason.MappingMismatch } };
        var engine = new ExpertDeliveryFacade(native); engine.Toggle(0); engine.Tick(600);
        Assert.Empty(native.Refreshes); Assert.Empty(native.Calls);
    }
    [Fact] public void CooldownEnforces580MillisecondsAndNextItemUsesNewGeneration()
    {
        var (native, engine) = Start(); Reward(native); engine.Tick(10);
        native.Value = native.Value with { Seals = 45000, PendingSource = null }; engine.Tick(20);
        native.Value = Ready(Row with { Source = Source with { Slot = 4 }, AgentIndex = 14 });
        engine.Tick(599); Assert.Equal(2, native.Calls.Count);
        engine.Tick(600); Assert.Equal(3, native.Calls.Count);
        Assert.True(native.Calls[2].Pending.Generation > native.Calls[0].Pending.Generation);
    }
    [Fact] public void FreshEmptyIsDifferentFromMappingFailure()
    {
        var native = new Fake { Value = Ready() with { State = DeliveryReadState.ReadyEmpty, Rows = [] } };
        var engine = new ExpertDeliveryFacade(native); engine.Toggle(0); engine.Tick(1);
        AssertReason(DeliveryReason.Empty, engine); Assert.Empty(native.Calls);
        native.Value = Ready() with { State = DeliveryReadState.Inconsistent, Rows = [], Reason = DeliveryReason.MappingMismatch };
        engine.Toggle(2); engine.Tick(3); AssertReason(DeliveryReason.MappingMismatch, engine);
    }
    [Fact] public void SourceMutationBeforeSubmitStops()
    {
        var (native, engine) = Start(); Reward(native);
        native.Value = native.Value with { PendingSource = Source with { Fingerprint = 992 } }; engine.Tick(10);
        Assert.Single(native.Calls); AssertReason(DeliveryReason.MappingMismatch, engine);
    }
    [Fact] public void FailedPossibleSubmitIsObservedWithoutRetry()
    {
        var (native, engine) = Start(); native.Result = new(false, true, DeliveryReason.DispatchRejected); Reward(native); engine.Tick(10);
        native.Value = native.Value with { Seals = 45000, PendingSource = null }; engine.Tick(20);
        Assert.Equal(1, engine.Status.ConfirmedCount); Assert.Equal(2, native.Calls.Count);
    }
    [Fact] public void MappingBindsSourcesUnderReorderingAndExcludesSupplyPrefix()
    {
        var random = new Random(17);
        for (var size = 1; size < 80; size++)
        {
            var order = Enumerable.Range(0, size).OrderBy(_ => random.Next()).ToArray();
            var rows = order.Select((id, visible) => Row with { VisibleIndex = visible, AgentIndex = id + 11, Source = Source with { Slot = id } }).ToArray();
            Assert.True(DeliveryRules.ValidMapping(rows));
            Assert.False(DeliveryRules.ValidMapping(rows.Select((row, i) => i == 0 ? row with { AgentIndex = 10 } : row).ToArray()));
            if (size > 1) { rows[1] = rows[1] with { Source = rows[0].Source }; Assert.False(DeliveryRules.ValidMapping(rows)); }
        }
    }
    [Fact] public void ExactRemovalRejectsReplacementOrWrongDecrement()
    {
        Assert.False(DeliveryRules.ExactRemoval(Source, Source with { ItemId = 321 }));
        var stack = Source with { Quantity = 3 };
        Assert.True(DeliveryRules.ExactRemoval(stack, stack with { Quantity = 2 }));
        Assert.False(DeliveryRules.ExactRemoval(stack, stack with { Quantity = 1 }));
    }
}
