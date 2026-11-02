using EZDelivery.Features.ExpertDelivery;

namespace EZDelivery.Tests.ExpertDelivery;

public sealed partial class DeliverySafetyTests
{
    [Theory]
    [InlineData(true, true, DeliveryDialogKind.SpiritbondWarning)]
    [InlineData(true, false, DeliveryDialogKind.Unrelated)]
    public void MateriaWarningRequiresBothExactCallbackAndPrompt(bool callback, bool prompt, DeliveryDialogKind expected)
        => Assert.Equal(expected, DeliveryRules.WarningKind(true, true, false, callback, false, prompt, false));

    [Theory]
    [InlineData(DeliveryReason.NoCharacter, "No character is available.")]
    [InlineData(DeliveryReason.UnsupportedBuild, "Expert Delivery is unavailable for this unreviewed game build.")]
    public void UnavailableReasonsGiveSpecificPlayerMessage(DeliveryReason reason, string expected)
        => Assert.Equal(expected, DeliveryRules.Message(reason));

    [Theory]
    [InlineData(DeliveryReadState.Failed, DeliveryReason.NotReady, DeliveryReason.NotReady)]
    [InlineData(DeliveryReadState.Unsupported, DeliveryReason.UnsupportedBuild, DeliveryReason.UnsupportedBuild)]
    [InlineData(DeliveryReadState.Inconsistent, DeliveryReason.None, DeliveryReason.NotReady)]
    public void PostReceiptInvalidReadStopsWithoutNextSelection(DeliveryReadState state, DeliveryReason observed, DeliveryReason expected)
    {
        var (native, engine) = Start(); Reward(native); engine.Tick(2);
        native.Value = native.Value with { PendingSource = null, Seals = 45000 }; engine.Tick(3);
        native.Value = Ready() with { State = state, Reason = observed };
        engine.Tick(583);
        Assert.Equal(expected, engine.Status.Reason);
        Assert.Equal(2, native.Calls.Count);
        Assert.Equal(1, engine.Status.ConfirmedCount);
    }

    [Fact]
    public void PostReceiptClosedAddonWaitsEvenWhenListStateClaimsReady()
    {
        var (native, engine) = Start(); Reward(native); engine.Tick(2);
        native.Value = native.Value with { PendingSource = null, Seals = 45000 }; engine.Tick(3);
        native.Value = Ready() with { Context = Context with { Addon = 0 } };
        engine.Tick(583);
        Assert.Equal(DeliveryPhase.Cooldown, engine.Status.Phase); Assert.Equal(2, native.Calls.Count);
        native.Value = Ready() with { State = DeliveryReadState.ReadyEmpty, Rows = [] };
        engine.Tick(584);
        AssertReason(DeliveryReason.Empty, engine); Assert.Equal(2, native.Calls.Count);
    }

    [Fact]
    public void RepeatedMateriaWarningIsAcknowledgedOnlyOnce()
    {
        var (native, engine) = Start();
        native.Value = native.Value with { Dialog = new(DeliveryDialogKind.SpiritbondWarning, 79, Source.ItemId, true, null, true) };
        engine.Tick(2); engine.Tick(3);
        Assert.Equal(2, native.Calls.Count); Assert.True(engine.IsRunning);
    }

    [Fact]
    public void UnknownDialogKindCannotBeSubmitted()
    {
        var (native, engine) = Start();
        native.Value = native.Value with { Dialog = new((DeliveryDialogKind)99, 79, Source.ItemId, true, 500, true) };
        engine.Tick(2);
        AssertReason(DeliveryReason.UnrelatedDialog, engine); Assert.Single(native.Calls);
    }

    [Fact]
    public void TemporarilyUnreadableInitialListWaitsThenTimesOut()
    {
        var native = new Fake { Value = Ready() with { State = DeliveryReadState.NotReady, Reason = DeliveryReason.None } };
        var engine = new ExpertDeliveryFacade(native); engine.Toggle(0);
        engine.Tick(4999); Assert.True(engine.IsRunning); Assert.Empty(native.Calls);
        engine.Tick(5000); AssertReason(DeliveryReason.NotReady, engine); Assert.Empty(native.Calls);
    }

    [Fact]
    public void ExistingRewardOnNewStartCannotAuthorizeItsSubmission()
    {
        var native = new Fake(); Reward(native);
        var engine = new ExpertDeliveryFacade(native); engine.Toggle(0); engine.Tick(1);
        AssertReason(DeliveryReason.UnrelatedDialog, engine); Assert.Empty(native.Calls);
    }

    [Fact]
    public void RejectionWithoutNativeReasonUsesExplicitDispatchFailure()
    {
        var native = new Fake { Result = new(false, false, DeliveryReason.None) };
        var engine = new ExpertDeliveryFacade(native); engine.Toggle(0); engine.Tick(1);
        AssertReason(DeliveryReason.DispatchRejected, engine);
        Assert.Single(native.Calls);
    }

    [Fact]
    public void MissingCharacterDuringReceiptWaitCannotConfirm()
    {
        var (native, engine) = Start(); Reward(native); engine.Tick(2);
        native.Value = native.Value with { Context = null, PendingSource = null, Seals = 45000 };
        engine.Tick(5002);
        AssertReason(DeliveryReason.OutcomeUncertain, engine);
        Assert.Equal(0, engine.Status.ConfirmedCount);
    }

    [Fact]
    public void TerritoryChangeAfterExactReceiptConfirmsButStopsRun()
    {
        var (native, engine) = Start(); Reward(native); engine.Tick(2);
        native.Value = native.Value with { Context = Context with { Territory = 999 }, PendingSource = null, Seals = 45000 };
        engine.Tick(3);
        AssertReason(DeliveryReason.WrongContext, engine);
        Assert.Equal(1, engine.Status.ConfirmedCount); Assert.Equal(2, native.Calls.Count);
    }

    [Fact]
    public void FailedReceiptSubscriberCannotRepeatTheDelivery()
    {
        var (native, engine) = Start(); Reward(native); engine.Tick(2);
        engine.Delivered += (_, _, _) => throw new InvalidOperationException("Report failed");
        native.Value = native.Value with { PendingSource = null, Seals = 45000 };
        Assert.Throws<InvalidOperationException>(() => engine.Tick(3));
        engine.Stop(); engine.Tick(4);
        Assert.False(engine.IsRunning);
        Assert.Equal(1, engine.Status.ConfirmedCount); Assert.Equal(2, native.Calls.Count);
    }
}
