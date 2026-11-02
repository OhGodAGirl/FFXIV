using EZDelivery.Features.ExpertDelivery;

namespace EZDelivery.Tests.ExpertDelivery;

public sealed partial class DeliverySafetyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SelectedNativeFilterAndSortReachExactSelectionUnchanged(int filter)
    {
        var first = Row with { Available = false };
        var selected = Row with { VisibleIndex = 1, AgentIndex = 13, Source = Source with { Slot = 9 } };
        var native = new Fake { Value = Ready(first, selected) with { Filter = filter, Sort = 3 } };
        var engine = new ExpertDeliveryFacade(native); engine.Toggle(0); engine.Tick(1);
        var command = Assert.Single(native.Calls);
        Assert.Equal(DeliveryCommandKind.Select, command.Kind);
        Assert.Equal(selected, command.Pending.Row);
        Assert.Equal(filter, command.Pending.Filter);
        Assert.Equal(3, command.Pending.Sort);
    }

    [Fact]
    public void OverflowOfFirstNativeEligibleItemNeverSkipsAhead()
    {
        var next = Row with { VisibleIndex = 1, AgentIndex = 13, Source = Source with { Slot = 9 }, Reward = 1 };
        var native = new Fake { Value = Ready(Row with { Reward = 501 }, next) };
        var engine = new ExpertDeliveryFacade(native); engine.Toggle(0); engine.Tick(1);
        Assert.Empty(native.Calls);
        AssertReason(DeliveryReason.SealCapacity, engine);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("unreadable")]
    [InlineData("item")]
    [InlineData("uncorrelated")]
    [InlineData("context")]
    [InlineData("sort")]
    public void PreSubmitIdentityChangesCannotAuthorizeDelivery(string change)
    {
        var (native, engine) = Start(); Reward(native);
        native.Value = change switch
        {
            "source" => native.Value with { PendingSource = null },
            "unreadable" => native.Value with { PendingSourceReadable = false },
            "item" => native.Value with { Dialog = native.Value.Dialog with { ItemId = 999 } },
            "uncorrelated" => native.Value with { Dialog = native.Value.Dialog with { Correlated = false } },
            "context" => native.Value with { Context = Context with { Rank = 10 } },
            _ => native.Value with { Sort = 3 },
        };
        engine.Tick(2);
        Assert.Single(native.Calls);
        Assert.False(engine.IsRunning);
        Assert.Equal(0, engine.Status.ConfirmedCount);
    }

    [Fact]
    public void DisabledOrMissingDialogWaitsOnlyUntilDeadline()
    {
        var (native, engine) = Start(); engine.Tick(2); Assert.Single(native.Calls);
        Reward(native); native.Value = native.Value with { Dialog = native.Value.Dialog with { Enabled = false } };
        engine.Tick(3); Assert.Single(native.Calls); Assert.True(engine.IsRunning);
        engine.Tick(5001); Assert.Single(native.Calls); AssertReason(DeliveryReason.NotReady, engine);
    }

    [Theory]
    [InlineData(DeliveryDialogKind.HqWarning)]
    [InlineData(DeliveryDialogKind.SpiritbondWarning)]
    public void RecreatedWarningCannotReceiveASecondAcknowledgement(DeliveryDialogKind kind)
    {
        var (native, engine) = Start();
        native.Value = native.Value with { Dialog = new(kind, 79, Source.ItemId, true, null, true) };
        engine.Tick(2);
        native.Value = native.Value with { Dialog = native.Value.Dialog with { Identity = 80 } };
        engine.Tick(3);
        Assert.Equal(2, native.Calls.Count);
        AssertReason(DeliveryReason.UnrelatedDialog, engine);
    }

    [Fact]
    public void NormalQualitySourceCannotAcceptHqWarning()
    {
        var source = Source with { Hq = false, RawItemId = Source.ItemId };
        var native = new Fake { Value = Ready(Row with { Source = source }) with { PendingSource = source } };
        var engine = new ExpertDeliveryFacade(native); engine.Toggle(0); engine.Tick(1);
        native.Value = native.Value with { Dialog = new(DeliveryDialogKind.HqWarning, 79, Source.ItemId, true, null, true) };
        engine.Tick(2);
        Assert.Single(native.Calls); AssertReason(DeliveryReason.UnrelatedDialog, engine);
    }

    [Fact]
    public void RepeatedToggleRequestsStopWithoutSelectingAgain()
    {
        var (native, engine) = Start(); engine.Toggle(2); engine.Stop(); engine.Tick(3);
        Assert.Single(native.Calls); AssertReason(DeliveryReason.Cancelled, engine);
        engine.Stop(); Assert.False(engine.IsRunning);
    }

    [Fact]
    public void DefiniteRejectedSubmitDoesNotBecomeAnUncertainHandIn()
    {
        var (native, engine) = Start(); Reward(native);
        native.Result = DeliveryDispatchResult.Rejected(DeliveryReason.DispatchRejected);
        engine.Tick(2); AssertReason(DeliveryReason.DispatchRejected, engine);
        native.Result = DeliveryDispatchResult.Sent; native.Value = Ready();
        engine.Toggle(3); engine.Tick(4);
        Assert.Equal(3, native.Calls.Count);
        Assert.Equal(DeliveryCommandKind.Select, native.Calls[2].Kind);
    }

    [Fact]
    public void SubmitExceptionStillReconcilesExactReceiptOnce()
    {
        var (native, engine) = Start(); Reward(native); native.ThrowDispatch = true;
        engine.Tick(2); Assert.Equal(DeliveryPhase.Reconcile, engine.Status.Phase);
        native.Value = native.Value with { PendingSource = null, Seals = 45000 };
        engine.Tick(3); engine.Tick(4);
        Assert.Equal(1, engine.Status.ConfirmedCount); Assert.Equal(2, native.Calls.Count);
    }

    [Fact]
    public void SelectionOrCancellationFailureCannotSubmit()
    {
        var native = new Fake { ThrowDispatch = true };
        var engine = new ExpertDeliveryFacade(native); engine.Toggle(0); engine.Tick(1);
        Assert.Single(native.Calls); AssertReason(DeliveryReason.DispatchRejected, engine);
        (native, engine) = Start(); Reward(native); native.ThrowDispatch = true;
        engine.Stop(); engine.Tick(2);
        AssertReason(DeliveryReason.Cancelled, engine);
        Assert.DoesNotContain(native.Calls, x => x.Kind == DeliveryCommandKind.Submit);
    }

    [Theory]
    [InlineData("character")]
    [InlineData("company")]
    [InlineData("unreadable")]
    [InlineData("wrongseals")]
    public void ReceiptMustRetainExactCharacterCompanySourceAndReward(string change)
    {
        var (native, engine) = Start(); Reward(native); engine.Tick(2);
        native.Value = native.Value with { PendingSource = null, Seals = 45000 };
        native.Value = change switch
        {
            "character" => native.Value with { Context = Context with { Character = 456 } },
            "company" => native.Value with { Context = Context with { Company = 2 } },
            "unreadable" => native.Value with { PendingSourceReadable = false },
            _ => native.Value with { Seals = 44999 },
        };
        engine.Tick(5002);
        Assert.Equal(0, engine.Status.ConfirmedCount); AssertReason(DeliveryReason.OutcomeUncertain, engine);
    }

    [Theory]
    [InlineData("notready")]
    [InlineData("rejected")]
    [InlineData("exception")]
    public void ReceiptRefreshFailureHasBoundedAndExplicitOutcome(string failure)
    {
        var (native, engine) = Start(); Reward(native); engine.Tick(2);
        native.Value = native.Value with { PendingSource = null, Seals = 45000 }; engine.Tick(3);
        native.Value = Ready() with { State = DeliveryReadState.Inconsistent, Reason = DeliveryReason.MappingMismatch };
        native.RefreshResult = DeliveryDispatchResult.Rejected(failure == "notready" ? DeliveryReason.NotReady : DeliveryReason.DispatchRejected);
        native.ThrowRefresh = failure == "exception";
        engine.Tick(583); Assert.Single(native.Refreshes);
        if (failure == "rejected") AssertReason(DeliveryReason.DispatchRejected, engine);
        else
        {
            Assert.True(engine.IsRunning);
            engine.Tick(5003);
            AssertReason(DeliveryReason.MappingMismatch, engine);
        }
        Assert.Equal(2, native.Calls.Count);
    }

    [Fact]
    public void NoAvailableNativeRowsAndInvalidMappingAreDistinct()
    {
        var native = new Fake { Value = Ready(Row with { Available = false }) };
        var engine = new ExpertDeliveryFacade(native); engine.Toggle(0); engine.Tick(1);
        AssertReason(DeliveryReason.Empty, engine); Assert.Empty(native.Calls);
        native.Value = Ready() with { Rows = [] };
        engine.Toggle(2); engine.Tick(3);
        AssertReason(DeliveryReason.MappingMismatch, engine); Assert.Empty(native.Calls);
    }

    [Theory]
    [InlineData(0u, 0u)]
    [InlineData(45001u, 45000u)]
    public void InvalidCapacityCannotSelectEvenWhenEstimateIsUnknown(uint seals, uint cap)
    {
        var native = new Fake { Value = Ready(Row with { Reward = null }) with { Seals = seals, SealCap = cap } };
        var engine = new ExpertDeliveryFacade(native); engine.Toggle(0); engine.Tick(1);
        Assert.Empty(native.Calls); AssertReason(DeliveryReason.RewardUnknown, engine);
    }

    [Fact]
    public void UnknownEstimateCanSelectButOnlyKnownFinalRewardCanSubmit()
    {
        var native = new Fake { Value = Ready(Row with { Reward = null }) };
        var engine = new ExpertDeliveryFacade(native); engine.Toggle(0); engine.Tick(1);
        Reward(native); engine.Tick(2);
        Assert.Equal(new[] { DeliveryCommandKind.Select, DeliveryCommandKind.Submit }, native.Calls.Select(x => x.Kind));
    }
}
