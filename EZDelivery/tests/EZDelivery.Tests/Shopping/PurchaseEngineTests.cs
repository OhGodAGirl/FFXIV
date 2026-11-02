using EZDelivery.Features.Shopping;
using Xunit;

namespace EZDelivery.Tests.Shopping;

public sealed partial class PurchaseEngineTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void ExactSameCharacterHandInClearsOnlyThatPurchaseSuppression()
    {
        var engine = new PurchaseEngine(); var first = Offer(); var second = Offer(2);
        engine.Start([first, second], Identity); Confirm(engine, first); Confirm(engine, second);
        engine.ConfirmDelivery(2, first.ItemId);
        Assert.Contains(first.ItemId, engine.Suppressed);
        engine.ConfirmDelivery(Identity.Character, first.ItemId);
        Assert.DoesNotContain(first.ItemId, engine.Suppressed);
        Assert.Contains(second.ItemId, engine.Suppressed);
        Assert.True(engine.ResetPurchaseTracking());
        Assert.Empty(engine.Suppressed);
    }

    [Fact]
    public void TrackingResetCannotInterruptPurchaseOrEraseUnknownOutcome()
    {
        var offer = Offer(); var engine = ReadyToReconcile(offer);
        Assert.False(engine.ResetPurchaseTracking());
        engine.Tick(Observation(offer), Missing, Now.AddSeconds(6));
        Assert.True(engine.ResetPurchaseTracking());
        engine.Start([offer], Identity);
        Assert.Null(engine.Tick(Observation(offer), Missing, Now.AddSeconds(7)));
        AssertPhase(PurchasePhase.Blocked, engine);
    }
    private static readonly ShopIdentity Identity = new(1, 100, 1, 11, 42);
    private static readonly PurchaseKnowledge Missing = new(PurchasePresence.Missing);
    private static void AssertPhase(PurchasePhase expected, PurchaseEngine engine)
    {
        Assert.Equal(expected, engine.Phase);
        Assert.False(string.IsNullOrWhiteSpace(engine.Status));
    }
    private static ShopOffer Offer(uint id = 1, uint cost = 100) => new($"offer:{id}", id, 1000, "Item", ShopCategory.Weapons, 1, 1, 0, 0, cost, 0, 1, 0, 0);
    private static PurchaseObservation Observation(ShopOffer offer, uint seals = 500, int count = 0)
        => new(true, "", Identity, offer.Tier, offer.NativeCategory, 3, offer.ItemId, offer.UnitCost, seals, count, true);

    [Fact]
    public void FixedQueueDeduplicatesAndNeverAddsLaterRows()
    {
        var first = Offer(); var second = Offer(2);
        var list = new List<ShopOffer> { first, first, second };
        var engine = new PurchaseEngine();
        Assert.True(engine.Start(list, Identity)); list.Add(Offer(3));
        Confirm(engine, first);
        Confirm(engine, second);
        AssertPhase(PurchasePhase.Completed, engine);
        Assert.Equal(2, engine.ConfirmedCount);
    }

    [Theory]
    [InlineData(499, 1)]
    [InlineData(400, 0)]
    [InlineData(500, 1)]
    [InlineData(400, 2)]
    public void NeitherPartialNorWrongDeltaConfirms(int seals, int count)
    {
        var offer = Offer(); var engine = ReadyToReconcile(offer);
        engine.Tick(Observation(offer, (uint)seals, count), Missing, Now.AddSeconds(6));
        Assert.Equal(0, engine.ConfirmedCount);
        AssertPhase(PurchasePhase.OutcomeUncertain, engine);
        Assert.True(engine.Start([offer], Identity));
        Assert.Null(engine.Tick(Observation(offer), Missing, Now.AddSeconds(7)));
        AssertPhase(PurchasePhase.Blocked, engine);
    }

    [Fact]
    public void StopAfterFinalDispatchReconcilesButDoesNotBuyNext()
    {
        var offer = Offer(); var engine = ReadyToReconcile(offer, Offer(2));
        engine.Stop();
        Assert.True(engine.IsRunning);
        engine.Tick(Observation(offer, 400, 1), Missing, Now);
        AssertPhase(PurchasePhase.Stopped, engine);
        Assert.Equal(1, engine.ConfirmedCount);
        Assert.Null(engine.Tick(Observation(Offer(2)), Missing, Now));
    }

    [Fact]
    public void UnrelatedDialogNeverReceivesConfirm()
    {
        var offer = Offer(); var engine = new PurchaseEngine(); engine.Start([offer], Identity);
        var request = engine.Tick(Observation(offer), Missing, Now)!;
        engine.Dispatched(request, new(true, true), Now);
        Assert.Null(engine.Tick(Observation(offer) with { Dialog = new(7, false, offer.ItemId, 1, offer.UnitCost) }, Missing, Now));
        AssertPhase(PurchasePhase.Blocked, engine);
    }

    [Fact]
    public void QuantityOrPriceMismatchNeverReceivesConfirm()
    {
        foreach (var dialog in new[] { new PurchaseDialog(7, true, 1, 2, 100), new PurchaseDialog(7, true, 1, 1, 101) })
        {
            var offer = Offer(); var engine = new PurchaseEngine(); engine.Start([offer], Identity);
            var request = engine.Tick(Observation(offer), Missing, Now)!;
            engine.Dispatched(request, new(true, true), Now);
            Assert.Null(engine.Tick(Observation(offer) with { Dialog = dialog }, Missing, Now));
            AssertPhase(PurchasePhase.Blocked, engine);
        }
    }

    [Fact]
    public void DispatchExceptionNeverRetries()
    {
        var offer = Offer(); var engine = new PurchaseEngine(); engine.Start([offer], Identity);
        var request = engine.Tick(Observation(offer), Missing, Now)!;
        engine.Dispatched(request, new(false, true), Now);
        AssertPhase(PurchasePhase.Reconcile, engine);
        Assert.Null(engine.Tick(Observation(offer), Missing, Now));
        Assert.Null(engine.Tick(Observation(offer), Missing, Now.AddSeconds(5)));
        AssertPhase(PurchasePhase.OutcomeUncertain, engine);
    }

    [Fact]
    public void ExactBalanceAllowedButUnaffordableNextStopsWithoutSkipping()
    {
        var offer = Offer(); var engine = new PurchaseEngine(); engine.Start([offer, Offer(2, 501), Offer(3, 1)], Identity);
        Confirm(engine, offer);
        Assert.Null(engine.Tick(Observation(Offer(2, 501), 400), Missing, Now.AddMilliseconds(580)));
        AssertPhase(PurchasePhase.Stopped, engine);
        var exact = new PurchaseEngine(); exact.Start([offer], Identity);
        Assert.Equal(ShopCommandKind.RequestOne, exact.Tick(Observation(offer, 100), Missing, Now)!.Kind);
    }

    [Fact]
    public void NavigationWaitsAndUsesExactDestination()
    {
        var offer = Offer() with { Tier = 2, NativeCategory = 3 };
        var engine = new PurchaseEngine(); engine.Start([offer], Identity);
        var before = Observation(offer) with { Tier = 0, NativeCategory = 0, NativeRow = -1 };
        var tier = engine.Tick(before, Missing, Now)!;
        Assert.Equal(ShopCommandKind.SelectTier, tier.Kind);
        engine.Dispatched(tier, new(true, true), Now);
        Assert.Null(engine.Tick(before, Missing, Now.AddSeconds(1)));
        var category = engine.Tick(before with { Tier = 2 }, Missing, Now.AddSeconds(1))!;
        Assert.Equal(ShopCommandKind.SelectCategory, category.Kind);
        engine.Dispatched(category, new(true, true), Now);
        Assert.Null(engine.Tick(before with { Tier = 2 }, Missing, Now.AddSeconds(3)));
        AssertPhase(PurchasePhase.Blocked, engine);
    }

    [Fact]
    public void DirectNativeRequestStillRequiresBothReceipts()
    {
        var offer = Offer(); var engine = new PurchaseEngine(); engine.Start([offer], Identity);
        var request = engine.Tick(Observation(offer), Missing, Now)!;
        engine.Dispatched(request, new(true, true, ExpectsConfirmation: false), Now);
        AssertPhase(PurchasePhase.Reconcile, engine);
        engine.Tick(Observation(offer, 400, 1), Missing, Now);
        Assert.Equal(1, engine.ConfirmedCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExchangeConfirmationWorksAloneOrAfterEquipmentWarning(bool equipmentFirst)
    {
        var offer = Offer(); var engine = new PurchaseEngine(); engine.Start([offer], Identity);
        var request = engine.Tick(Observation(offer), Missing, Now)!;
        engine.Dispatched(request, new(true, true, ExpectsConfirmation: false), Now);
        if (equipmentFirst)
        {
            var equipment = engine.Tick(Observation(offer) with { Dialog = new(9, true, offer.ItemId, 1, offer.UnitCost, PurchaseDialogKind.Equipment) }, Missing, Now)!;
            engine.Dispatched(equipment, new(true, true), Now);
        }
        var observation = Observation(offer) with { Dialog = new(10, true, offer.ItemId, 1, offer.UnitCost, PurchaseDialogKind.Exchange) };
        var confirm = engine.Tick(observation, Missing, Now)!;
        Assert.Equal(ShopCommandKind.ConfirmExchange, confirm.Kind);
        engine.Dispatched(confirm, new(true, true), Now);
        Assert.Null(engine.Tick(observation, Missing, Now));
        Assert.Equal(0, engine.ConfirmedCount);
        engine.Tick(Observation(offer, 400, 1), Missing, Now);
        Assert.Equal(1, engine.ConfirmedCount);
    }

    [Fact]
    public void UnknownKnowledgeAndChangedContextPreventCalls()
    {
        foreach (var observation in new[] { Observation(Offer()), Observation(Offer()) with { Identity = Identity with { Rank = 10 } } })
        {
            var engine = new PurchaseEngine(); engine.Start([Offer()], Identity);
            Assert.Null(engine.Tick(observation, new(PurchasePresence.Unknown), Now));
            AssertPhase(PurchasePhase.Blocked, engine);
        }
    }

    [Fact]
    public void DirectRequestEquipmentWarningConfirmsOnceThenRequiresBothReceipts()
    {
        var offer = Offer(); var engine = new PurchaseEngine(); engine.Start([offer], Identity);
        var request = engine.Tick(Observation(offer), Missing, Now)!;
        engine.Dispatched(request, new(true, true, ExpectsConfirmation: false), Now);
        var warning = Observation(offer) with { Dialog = new(9, true, offer.ItemId, 1, offer.UnitCost, PurchaseDialogKind.Equipment) };
        var confirm = engine.Tick(warning, Missing, Now)!;
        Assert.Equal(ShopCommandKind.ConfirmEquipment, confirm.Kind);
        Assert.Null(engine.Tick(warning, Missing, Now));
        engine.Dispatched(confirm, new(true, true), Now);
        Assert.Null(engine.Tick(warning, Missing, Now));
        Assert.Equal(0, engine.ConfirmedCount);
        engine.Tick(Observation(offer, 400, 1), Missing, Now);
        Assert.Equal(1, engine.ConfirmedCount);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("owner")]
    [InlineData("seals")]
    [InlineData("item")]
    [InlineData("context")]
    [InlineData("knowledge")]
    [InlineData("expired")]
    public void EquipmentWarningCannotBypassPendingPurchaseGuards(string change)
    {
        var offer = Offer(); var engine = new PurchaseEngine(); engine.Start([offer], Identity);
        var request = engine.Tick(Observation(offer), Missing, Now)!;
        engine.Dispatched(request, new(true, true, ExpectsConfirmation: false), Now);
        var warning = Observation(offer) with { Dialog = new(9, true, offer.ItemId, 1, offer.UnitCost, PurchaseDialogKind.Equipment) };
        if (change == "stop") engine.Stop();
        if (change == "owner") warning = warning with { Dialog = warning.Dialog! with { Correlated = false } };
        if (change == "seals") warning = warning with { Seals = 499 };
        if (change == "item") warning = warning with { ItemCount = 1 };
        if (change == "context") warning = warning with { Identity = Identity with { Addon = 43 } };
        Assert.Null(engine.Tick(warning, change == "knowledge" ? new(PurchasePresence.Unknown) : Missing,
            change == "expired" ? Now.AddSeconds(6) : Now));
        Assert.Equal(0, engine.ConfirmedCount);
    }

    [Fact]
    public void EquipmentWarningAfterCurrencyConfirmationStillNeedsItsOwnAcknowledgement()
    {
        var offer = Offer();
        var engine = ReadyToReconcile(offer);
        var warning = Observation(offer) with { Dialog = new(9, true, offer.ItemId, 1, offer.UnitCost, PurchaseDialogKind.Equipment) };
        var confirm = engine.Tick(warning, Missing, Now)!;
        Assert.Equal(ShopCommandKind.ConfirmEquipment, confirm.Kind);
        engine.Dispatched(confirm, new(true, true), Now);
        Assert.Null(engine.Tick(warning, Missing, Now.AddSeconds(6)));
        AssertPhase(PurchasePhase.OutcomeUncertain, engine);
        Assert.Equal(0, engine.ConfirmedCount);
    }

    [Fact]
    public void DuplicateTickCannotDispatchAgainBeforeReceipt()
    {
        var engine = new PurchaseEngine(); engine.Start([Offer()], Identity);
        Assert.NotNull(engine.Tick(Observation(Offer()), Missing, Now));
        Assert.Null(engine.Tick(Observation(Offer()), Missing, Now));
        Assert.False(engine.Start([Offer()], Identity));
    }

    [Fact]
    public void GeneratedCatalogClassificationHasNoOverlappingEquipmentCategory()
    {
        for (uint category = 0; category < 120; category++)
        {
            Assert.Equal(ShopCategory.Weapons, CatalogClassification.Classify(true, true, category, 1013, 2, true));
            Assert.Equal(ShopCategory.Armor, CatalogClassification.Classify(false, true, category, 1013, 2, true));
            Assert.Equal(ShopCategory.Barding, CatalogClassification.Classify(false, false, category, 1013, 2, true));
        }
        Assert.Null(CatalogClassification.Classify(false, false, 63, 1013, 2, false));
        Assert.Null(CatalogClassification.Classify(false, false, 61, 0, 0, false));
        Assert.Equal(ShopCategory.Furniture, CatalogClassification.Classify(false, false, 57, 0, 0, false));
    }

    [Fact]
    public void SnapshotRejectsZeroCostAndOversizedQueue()
    {
        Assert.Throws<ArgumentException>(() => CatalogClassification.Snapshot([Offer(1, 0)]));
        Assert.Throws<ArgumentException>(() => CatalogClassification.Snapshot(Enumerable.Range(1, 2049).Select(x => Offer((uint)x))));
    }

    private static PurchaseEngine ReadyToReconcile(ShopOffer offer, params ShopOffer[] tail)
    {
        var engine = new PurchaseEngine(); engine.Start(new[] { offer }.Concat(tail), Identity);
        Assert.Equal("Checking next item", engine.Status);
        var request = engine.Tick(Observation(offer), Missing, Now)!;
        engine.Dispatched(request, new(true, true), Now);
        Assert.Equal("Waiting for purchase confirmation", engine.Status);
        var confirm = engine.Tick(Observation(offer) with { Dialog = new(7, true, offer.ItemId, 1, offer.UnitCost) }, Missing, Now)!;
        Assert.Equal(ShopCommandKind.ConfirmOnce, confirm.Kind);
        engine.Dispatched(confirm, new(true, true), Now);
        Assert.Equal("Checking inventory and seals", engine.Status);
        return engine;
    }

    private static void Confirm(PurchaseEngine engine, ShopOffer offer)
    {
        var now = Now.AddSeconds(engine.ConfirmedCount);
        var request = engine.Tick(Observation(offer), Missing, now)!;
        Assert.Equal(ShopCommandKind.RequestOne, request.Kind);
        engine.Dispatched(request, new(true, true), now);
        var confirm = engine.Tick(Observation(offer) with { Dialog = new(7, true, offer.ItemId, 1, offer.UnitCost) }, Missing, now)!;
        engine.Dispatched(confirm, new(true, true), now);
        engine.Tick(Observation(offer, 500 - offer.UnitCost, 1), Missing, now);
    }

    [Fact]
    public void NextPurchaseWaitsFromReceiptAndStopRemainsImmediate()
    {
        var first = Offer(); var second = Offer(2);
        var engine = ReadyToReconcile(first, second);
        engine.Tick(Observation(first, 400, 1), Missing, Now.AddSeconds(2));
        Assert.Null(engine.Tick(Observation(second), Missing, Now.AddMilliseconds(2579)));
        Assert.Equal(ShopCommandKind.RequestOne, engine.Tick(Observation(second), Missing, Now.AddMilliseconds(2580))!.Kind);

        engine = ReadyToReconcile(first, second);
        engine.Tick(Observation(first, 400, 1), Missing, Now);
        engine.Stop();
        Assert.False(engine.IsRunning);
        Assert.Null(engine.Tick(Observation(second), Missing, Now.AddSeconds(1)));
    }
}
