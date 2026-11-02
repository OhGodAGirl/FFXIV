using EZDelivery.Features.Shopping;

namespace EZDelivery.Tests.Shopping;

public sealed partial class PurchaseEngineTests
{
    [Fact]
    public void PurchaseStatesKeepSpecificUsableCopy()
    {
        var offer = Offer(); var engine = new PurchaseEngine(); Assert.Equal("Ready", engine.Status);
        Assert.Empty(engine.Suppressed);
        engine.Start([offer], Identity);
        Assert.Null(engine.Tick(Observation(offer) with { Ready = false }, Missing, Now)); Assert.Equal("Shop context changed.", engine.Status);
        engine.Start([offer], Identity);
        Assert.Null(engine.Tick(Observation(offer), new(PurchasePresence.Unknown), Now)); Assert.Equal("Missing items cannot be determined yet.", engine.Status);
        engine.Start([offer], Identity);
        var request = engine.Tick(Observation(offer), Missing, Now)!;
        engine.Dispatched(request, new(false, false), Now); Assert.Equal("The native action was rejected.", engine.Status);
        engine.Start([offer], Identity);
        request = engine.Tick(Observation(offer), Missing, Now)!;
        engine.Dispatched(request, new(false, true), Now); Assert.Equal("Checking the possibly sent purchase; no further actions will be sent.", engine.Status);
    }

    [Fact]
    public void StopBeforeAnyDispatchProvidesAStoppedStatus()
    {
        var engine = new PurchaseEngine(); engine.Start([Offer()], Identity); engine.Stop();
        AssertPhase(PurchasePhase.Stopped, engine);
    }

    [Fact]
    public void ReceiptSpendingLastSealConfirmsAnOfferAtExactRankAndFirstNativeRow()
    {
        var offer = Offer() with { RequiredRank = Identity.Rank }; var engine = new PurchaseEngine();
        engine.Start([offer], Identity);
        var observation = Observation(offer, 100) with { NativeRow = 0 };
        var request = engine.Tick(observation, Missing, Now)!; Assert.Equal(ShopCommandKind.RequestOne, request.Kind);
        engine.Dispatched(request, new(true, true, ExpectsConfirmation: false), Now);
        var warning = engine.Tick(observation with { Dialog = new(7, true, offer.ItemId, 1, offer.UnitCost, PurchaseDialogKind.Equipment) }, Missing, Now)!;
        Assert.Equal(ShopCommandKind.ConfirmEquipment, warning.Kind); engine.Dispatched(warning, new(true, true), Now);
        engine.Tick(observation with { Seals = 0, ItemCount = 1 }, Missing, Now);
        AssertPhase(PurchasePhase.Completed, engine); Assert.Equal(1, engine.ConfirmedCount);
    }

    [Fact]
    public void WarningAtExactReceiptDeadlineCannotReceiveConfirmation()
    {
        var offer = Offer(); var engine = ReadyToReconcile(offer);
        var command = engine.Tick(Observation(offer) with { Dialog = new(7, true, offer.ItemId, 1, offer.UnitCost, PurchaseDialogKind.Exchange) }, Missing, Now.AddSeconds(5));
        Assert.Null(command); AssertPhase(PurchasePhase.OutcomeUncertain, engine);
    }

    [Fact]
    public void ReceiptAndCooldownExposeProgressBeforeNextFixedItem()
    {
        var first = Offer(); var second = Offer(2); var engine = ReadyToReconcile(first, second);
        engine.Tick(Observation(first, 400, 1), Missing, Now);
        Assert.Equal("Purchase confirmed", engine.Status);
        Assert.Null(engine.Tick(Observation(second), Missing, Now.AddMilliseconds(579)));
        Assert.Equal("Purchase confirmed; waiting for the shop to settle", engine.Status);
    }

    [Fact]
    public void NativeNavigationHasUsableStatusAndThirdCompanyRemainsValid()
    {
        var identity = Identity with { Company = 3 }; var offer = Offer() with { Company = 3, Tier = 1 };
        var engine = new PurchaseEngine(); Assert.True(engine.Start([offer], identity));
        var observation = Observation(offer) with { Identity = identity, Tier = 0 };
        var select = engine.Tick(observation, Missing, Now)!;
        engine.Dispatched(select, new(true, true), Now);
        Assert.Equal("Changing native shop tab", engine.Status);
        Assert.Equal(3, Assert.Single(CatalogClassification.Snapshot([offer])).Company);
    }

    [Fact]
    public void DirectCharacterRestartClearsOldSuppressionAndUncertainty()
    {
        var offer = Offer(); var engine = new PurchaseEngine(); engine.Start([offer], Identity); Confirm(engine, offer);
        var nextIdentity = Identity with { Character = 2 };
        engine.Start([offer], nextIdentity);
        Assert.Empty(engine.Suppressed);
        var request = engine.Tick(Observation(offer) with { Identity = nextIdentity }, Missing, Now.AddSeconds(2))!;
        Assert.NotNull(request); engine.Dispatched(request, new(false, true), Now.AddSeconds(2));
        engine.Tick(Observation(offer) with { Identity = nextIdentity }, Missing, Now.AddSeconds(7));
        AssertPhase(PurchasePhase.OutcomeUncertain, engine);
        engine.Start([offer], Identity);
        Assert.NotNull(engine.Tick(Observation(offer), Missing, Now.AddSeconds(8)));
    }

    [Fact]
    public void ClearingCharacterForgetsUncertainItemsForNewSession()
    {
        var offer = Offer(); var engine = ReadyToReconcile(offer);
        engine.Tick(Observation(offer), Missing, Now.AddSeconds(5));
        engine.ClearCharacter(0);
        engine.Start([offer], Identity);
        Assert.NotNull(engine.Tick(Observation(offer), Missing, Now.AddSeconds(6)));
    }

    [Fact]
    public void InventoryConflictDuringWarningReturnsNoCommand()
    {
        var offer = Offer(); var engine = ReadyToReconcile(offer);
        var observation = Observation(offer) with { HasCapacity = false, Dialog = new(7, true, offer.ItemId, 1, offer.UnitCost, PurchaseDialogKind.Exchange) };
        Assert.Null(engine.Tick(observation, Missing, Now));
        AssertPhase(PurchasePhase.OutcomeUncertain, engine);
    }

    [Fact]
    public void CatalogFailuresAndPromptNormalizationRetainTheirMeaning()
    {
        Assert.Null(CatalogClassification.Classify(false, false, 0, 1, 2, true));
        Assert.Null(CatalogClassification.Classify(false, false, 0, 1013, 0, true));
        Assert.False(string.IsNullOrWhiteSpace(Assert.Throws<ArgumentException>(() => CatalogClassification.Snapshot([Offer(0)])).Message));
        Assert.False(string.IsNullOrWhiteSpace(Assert.Throws<ArgumentException>(() => CatalogClassification.Snapshot(Enumerable.Range(1, 2049).Select(id => Offer((uint)id)))).Message));
        Assert.Equal("Proceed with the transaction?", ShopPrompt.Normalize("  Proceed\r\nwith\tthe transaction?  "));
    }
}
