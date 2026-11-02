using EZDelivery.Features.Shopping;

namespace EZDelivery.Tests.Shopping;

public sealed partial class PurchaseEngineTests
{
    [Theory]
    [InlineData(4999, true)]
    [InlineData(5000, false)]
    [InlineData(5001, false)]
    public void ConfirmationDeadlineCannotBeBypassedByALateMatchingDialog(int elapsed, bool allowed)
    {
        var offer = Offer(); var engine = new PurchaseEngine(); engine.Start([offer], Identity);
        var request = engine.Tick(Observation(offer), Missing, Now)!;
        engine.Dispatched(request, new(true, true), Now);
        var command = engine.Tick(Observation(offer) with { Dialog = new(7, true, offer.ItemId, 1, offer.UnitCost) }, Missing, Now.AddMilliseconds(elapsed));
        Assert.Equal(allowed, command != null);
        if (allowed) Assert.Equal(ShopCommandKind.ConfirmOnce, command!.Kind);
        else AssertPhase(PurchasePhase.Blocked, engine);
    }

    [Theory]
    [InlineData(true, 1999, true)]
    [InlineData(true, 2000, false)]
    [InlineData(false, 1999, true)]
    [InlineData(false, 2000, false)]
    public void NavigationDeadlineCannotBeBypassedByLateReadiness(bool tier, int elapsed, bool allowed)
    {
        var offer = Offer() with { Tier = 2, NativeCategory = 3 };
        var engine = new PurchaseEngine(); engine.Start([offer], Identity);
        var before = Observation(offer) with { Tier = tier ? 0 : 2, NativeCategory = 0 };
        var select = engine.Tick(before, Missing, Now)!;
        Assert.Equal(tier ? ShopCommandKind.SelectTier : ShopCommandKind.SelectCategory, select.Kind);
        engine.Dispatched(select, new(true, true), Now);
        var command = engine.Tick(Observation(offer), Missing, Now.AddMilliseconds(elapsed));
        Assert.Equal(allowed, command != null);
        if (allowed) Assert.Equal(ShopCommandKind.RequestOne, command!.Kind);
        else AssertPhase(PurchasePhase.Blocked, engine);
    }

    [Theory]
    [InlineData(ShopCommandKind.RequestOne)]
    [InlineData(ShopCommandKind.ConfirmOnce)]
    [InlineData(ShopCommandKind.ConfirmEquipment)]
    [InlineData(ShopCommandKind.ConfirmExchange)]
    public void AmbiguousActionObservesReceiptWithoutConfirmingOrBuyingAgain(ShopCommandKind failedKind)
    {
        var offer = Offer(); var engine = new PurchaseEngine(); engine.Start([offer, Offer(2)], Identity);
        var command = engine.Tick(Observation(offer), Missing, Now)!;
        if (failedKind != ShopCommandKind.RequestOne)
        {
            engine.Dispatched(command, new(true, true), Now);
            var kind = failedKind switch
            {
                ShopCommandKind.ConfirmEquipment => PurchaseDialogKind.Equipment,
                ShopCommandKind.ConfirmExchange => PurchaseDialogKind.Exchange,
                _ => PurchaseDialogKind.Currency,
            };
            command = engine.Tick(Observation(offer) with { Dialog = new(7, true, offer.ItemId, 1, offer.UnitCost, kind) }, Missing, Now)!;
        }
        Assert.Equal(failedKind, command.Kind);
        engine.Dispatched(command, new(false, true), Now);
        AssertPhase(PurchasePhase.Reconcile, engine);
        Assert.False(engine.Start([offer], Identity));
        Assert.Null(engine.Tick(Observation(offer) with { Dialog = new(8, true, offer.ItemId, 1, offer.UnitCost, PurchaseDialogKind.Exchange) }, Missing, Now.AddSeconds(1)));
        Assert.Equal(0, engine.ConfirmedCount);
        Assert.Null(engine.Tick(Observation(offer, 400, 1), Missing, Now.AddSeconds(2)));
        Assert.Equal(1, engine.ConfirmedCount);
        Assert.Contains(offer.ItemId, engine.Suppressed);
        AssertPhase(PurchasePhase.Stopped, engine);
        Assert.Null(engine.Tick(Observation(Offer(2)), Missing, Now.AddSeconds(3)));
    }

    [Fact]
    public void AmbiguousActionWithOnlyOneReceiptTimesOutAndBlocksFreshRestart()
    {
        var offer = Offer(); var engine = new PurchaseEngine(); engine.Start([offer], Identity);
        var request = engine.Tick(Observation(offer), Missing, Now)!;
        engine.Dispatched(request, new(false, true), Now);
        Assert.Null(engine.Tick(Observation(offer, 400), Missing, Now.AddMilliseconds(4999)));
        AssertPhase(PurchasePhase.Reconcile, engine);
        engine.Tick(Observation(offer, 400), Missing, Now.AddSeconds(5));
        AssertPhase(PurchasePhase.OutcomeUncertain, engine);
        Assert.True(engine.Start([offer], Identity));
        Assert.Null(engine.Tick(Observation(offer), Missing, Now.AddSeconds(6)));
        AssertPhase(PurchasePhase.Blocked, engine);
    }

    [Fact]
    public void RejectedRequestDoesNotReserveOrSuppressAnUnsentPurchase()
    {
        var offer = Offer(); var engine = new PurchaseEngine(); engine.Start([offer], Identity);
        var request = engine.Tick(Observation(offer), Missing, Now)!;
        engine.Dispatched(request, new(false, false, "Rejected"), Now);
        AssertPhase(PurchasePhase.Blocked, engine);
        Assert.Equal("Rejected", engine.Status);
        Assert.Empty(engine.Suppressed);
        Assert.True(engine.Start([offer], Identity));
        Assert.NotNull(engine.Tick(Observation(offer), Missing, Now));
    }

    [Fact]
    public void OldAndDuplicateDispatchCompletionsCannotAdvanceANewOperation()
    {
        var offer = Offer(); var engine = new PurchaseEngine(); engine.Start([offer], Identity);
        var old = engine.Tick(Observation(offer), Missing, Now)!;
        engine.Dispatched(old, new(false, false), Now);
        engine.Start([offer], Identity);
        var current = engine.Tick(Observation(offer), Missing, Now)!;
        Assert.True(current.Generation > old.Generation);
        engine.Dispatched(old, new(true, true), Now);
        AssertPhase(PurchasePhase.ValidateNext, engine);
        Assert.Null(engine.Tick(Observation(offer), Missing, Now));
        engine.Dispatched(current, new(true, true), Now);
        engine.Dispatched(current, new(false, true), Now);
        AssertPhase(PurchasePhase.WaitConfirmation, engine);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("rank")]
    [InlineData("identity")]
    [InlineData("unready")]
    [InlineData("capacity")]
    [InlineData("count")]
    [InlineData("row")]
    [InlineData("price")]
    [InlineData("item")]
    public void ChangedPurchasePrerequisitesDispatchNothing(string change)
    {
        var offer = Offer();
        if (change == "company") offer = offer with { Company = 2 };
        if (change == "rank") offer = offer with { RequiredRank = 12 };
        var observation = Observation(offer);
        observation = change switch
        {
            "identity" => observation with { Identity = Identity with { Territory = 101 } },
            "unready" => observation with { Ready = false },
            "capacity" => observation with { HasCapacity = false },
            "count" => observation with { ItemCount = -1 },
            "row" => observation with { NativeRow = -1 },
            "price" => observation with { LiveCost = 101 },
            "item" => observation with { LiveItemId = 2 },
            _ => observation,
        };
        var engine = new PurchaseEngine(); engine.Start([offer], Identity);
        Assert.Null(engine.Tick(observation, Missing, Now));
        AssertPhase(PurchasePhase.Blocked, engine);
        Assert.Empty(engine.Suppressed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PositiveEvidenceSkipsOnlyItsFixedQueueItem(bool provider)
    {
        var first = Offer(); var second = Offer(2); var engine = new PurchaseEngine();
        engine.Start([first, second], Identity);
        Assert.Null(engine.Tick(Observation(first, count: provider ? 0 : 1), provider ? new(PurchasePresence.Present) : Missing, Now));
        Assert.Equal(second, engine.Pending);
        Assert.Equal(ShopCommandKind.RequestOne, engine.Tick(Observation(second), Missing, Now)!.Kind);
    }

    [Fact]
    public void ClosedShopCanConfirmAPendingReceiptButCannotBuyAnotherItem()
    {
        var offer = Offer(); var engine = ReadyToReconcile(offer, Offer(2));
        engine.Tick(Observation(offer, 400, 1) with { Ready = false, Identity = Identity with { Addon = 0 } }, Missing, Now);
        Assert.Equal(1, engine.ConfirmedCount);
        AssertPhase(PurchasePhase.Stopped, engine);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReceiptCannotCrossCharacterOrCompany(bool character)
    {
        var offer = Offer(); var engine = ReadyToReconcile(offer);
        var changed = character ? Identity with { Character = 2 } : Identity with { Company = 2 };
        engine.Tick(Observation(offer, 400, 1) with { Identity = changed }, Missing, Now);
        Assert.Equal(0, engine.ConfirmedCount);
        AssertPhase(PurchasePhase.OutcomeUncertain, engine);
    }

    [Fact]
    public void SinglePurchaseRestartAlsoRespectsReceiptCadence()
    {
        var first = Offer(); var second = Offer(2); var engine = new PurchaseEngine();
        engine.Start([first], Identity); Confirm(engine, first);
        Assert.True(engine.Start([second], Identity));
        Assert.Null(engine.Tick(Observation(second), Missing, Now.AddMilliseconds(579)));
        Assert.Equal(ShopCommandKind.RequestOne, engine.Tick(Observation(second), Missing, Now.AddMilliseconds(580))!.Kind);
    }

    [Fact]
    public void CharacterChangeClearsAcquisitionSuppressionAndStopsPendingWork()
    {
        var offer = Offer(); var engine = new PurchaseEngine(); engine.Start([offer], Identity); Confirm(engine, offer);
        engine.ClearCharacter(Identity.Character);
        Assert.Contains(offer.ItemId, engine.Suppressed);
        engine.ClearCharacter(2);
        Assert.Empty(engine.Suppressed);
        engine.Start([offer], Identity);
        engine.ClearCharacter(2);
        AssertPhase(PurchasePhase.Stopped, engine);
        Assert.Null(engine.Tick(Observation(offer), Missing, Now));
    }
}
