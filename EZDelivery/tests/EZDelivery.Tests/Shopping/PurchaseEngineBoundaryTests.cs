using EZDelivery.Features.Shopping;

namespace EZDelivery.Tests.Shopping;

public sealed partial class PurchaseEngineTests
{
    [Theory]
    [InlineData("character")]
    [InlineData("company-low")]
    [InlineData("company-high")]
    [InlineData("addon")]
    public void MissingShopIdentityCannotStart(string missing)
    {
        var current = missing switch
        {
            "character" => Identity with { Character = 0 },
            "company-low" => Identity with { Company = 0 },
            "company-high" => Identity with { Company = 4 },
            _ => Identity with { Addon = 0 },
        };
        var engine = new PurchaseEngine();
        Assert.Null(engine.Pending);
        Assert.False(engine.Start([Offer()], current));
        AssertPhase(PurchasePhase.Blocked, engine);
        Assert.Null(engine.Pending);
    }

    [Fact]
    public void EmptyQueueAndEntirelyPresentQueueCompleteWithoutDispatch()
    {
        var engine = new PurchaseEngine();
        Assert.False(engine.Start([], Identity));
        AssertPhase(PurchasePhase.Completed, engine);
        Assert.Null(engine.Pending);
        Assert.True(engine.Start([Offer()], Identity));
        Assert.Null(engine.Tick(Observation(Offer()), new(PurchasePresence.Present), Now));
        AssertPhase(PurchasePhase.Completed, engine);
        Assert.Equal(0, engine.ConfirmedCount);
        Assert.Null(engine.Pending);
    }

    [Fact]
    public void LostCharacterWhileReconcilingRecordsUncertainty()
    {
        var engine = ReadyToReconcile(Offer());
        engine.ClearCharacter(0);
        AssertPhase(PurchasePhase.OutcomeUncertain, engine);
        Assert.Null(engine.Pending);
        Assert.Null(engine.Tick(Observation(Offer(), 400, 1), Missing, Now));
        Assert.Equal(0, engine.ConfirmedCount);
    }

    [Fact]
    public void AcquiredEventFollowsBothReceiptsAndFiresOnce()
    {
        var offer = Offer(); var engine = ReadyToReconcile(offer);
        var acquired = new List<ShopOffer>(); engine.Acquired += acquired.Add;
        engine.Tick(Observation(offer, 400), Missing, Now);
        Assert.Empty(acquired);
        engine.Tick(Observation(offer, 400, 1), Missing, Now);
        engine.Tick(Observation(offer, 400, 1), Missing, Now.AddSeconds(1));
        Assert.Equal(offer, Assert.Single(acquired));
        Assert.Contains(offer.ItemId, engine.Suppressed);
    }

    [Fact]
    public void CurrencyDialogDuringReconciliationDoesNotReceiveAnotherConfirmation()
    {
        var offer = Offer(); var engine = ReadyToReconcile(offer);
        Assert.Null(engine.Tick(Observation(offer) with { Dialog = new(9, true, offer.ItemId, 1, offer.UnitCost) }, Missing, Now));
        AssertPhase(PurchasePhase.Reconcile, engine);
        Assert.Equal(0, engine.ConfirmedCount);
    }

    [Fact]
    public void StopDuringUnacknowledgedDispatchPreventsItsConfirmation()
    {
        var offer = Offer(); var engine = new PurchaseEngine(); engine.Start([offer], Identity);
        var request = engine.Tick(Observation(offer), Missing, Now)!;
        engine.Stop();
        engine.Dispatched(request, new(true, true), Now);
        Assert.Null(engine.Tick(Observation(offer) with { Dialog = new(9, true, offer.ItemId, 1, offer.UnitCost) }, Missing, Now));
        AssertPhase(PurchasePhase.Stopped, engine);
    }

    [Fact]
    public void ContextAndKnowledgeFailuresPreserveSpecificReasons()
    {
        var engine = new PurchaseEngine(); engine.Start([Offer()], Identity);
        Assert.Null(engine.Tick(Observation(Offer()) with { Ready = false, Reason = "Native shop row storage is unavailable." }, Missing, Now));
        Assert.Equal("Native shop row storage is unavailable.", engine.Status);
        engine.Start([Offer()], Identity);
        Assert.Null(engine.Tick(Observation(Offer()), new(PurchasePresence.Unknown, "Retainer snapshot is incomplete."), Now));
        Assert.Equal("Retainer snapshot is incomplete.", engine.Status);
    }

    [Theory]
    [InlineData("tier")]
    [InlineData("category")]
    [InlineData("count")]
    [InlineData("seals")]
    public void ResourceOrOfferChangeBeforeCurrencyConfirmationStops(string change)
    {
        var offer = Offer(); var engine = new PurchaseEngine(); engine.Start([offer], Identity);
        var request = engine.Tick(Observation(offer), Missing, Now)!;
        engine.Dispatched(request, new(true, true), Now);
        var observation = Observation(offer) with { Dialog = new(9, true, offer.ItemId, 1, offer.UnitCost) };
        observation = change switch
        {
            "tier" => observation with { Tier = 1 },
            "category" => observation with { NativeCategory = 1 },
            "count" => observation with { ItemCount = 1 },
            _ => observation with { Seals = 499 },
        };
        Assert.Null(engine.Tick(observation, Missing, Now));
        AssertPhase(PurchasePhase.Blocked, engine);
        Assert.Equal("The offer or resources changed before confirmation.", engine.Status);
    }

    [Fact]
    public void AbsentConfirmationWaitsAndExistingConfirmationBlocksNewRequest()
    {
        var offer = Offer(); var engine = new PurchaseEngine(); engine.Start([offer], Identity);
        var request = engine.Tick(Observation(offer), Missing, Now)!;
        engine.Dispatched(request, new(true, true), Now);
        Assert.Null(engine.Tick(Observation(offer), Missing, Now.AddSeconds(1)));
        AssertPhase(PurchasePhase.WaitConfirmation, engine);
        engine.Stop(); engine.Start([offer], Identity);
        Assert.Null(engine.Tick(Observation(offer) with { Dialog = new(9, true, offer.ItemId, 1, offer.UnitCost) }, Missing, Now));
        AssertPhase(PurchasePhase.Blocked, engine);
    }

    [Fact]
    public void CategoryWaitRejectsRankChangeAndWaitsForExactCategory()
    {
        var offer = Offer() with { Tier = 1, NativeCategory = 2 };
        var engine = new PurchaseEngine(); engine.Start([offer], Identity);
        var before = Observation(offer) with { NativeCategory = 0 };
        var command = engine.Tick(before, Missing, Now)!;
        engine.Dispatched(command, new(true, true), Now);
        Assert.Null(engine.Tick(before, Missing, Now.AddMilliseconds(500)));
        AssertPhase(PurchasePhase.WaitCategory, engine);
        Assert.Null(engine.Tick(before with { Tier = 0 }, Missing, Now.AddMilliseconds(1000)));
        AssertPhase(PurchasePhase.Blocked, engine);
    }

    [Fact]
    public void PossiblySentNavigationCannotBecomeAPurchaseReceipt()
    {
        var offer = Offer() with { Tier = 1 };
        var engine = new PurchaseEngine(); engine.Start([offer], Identity);
        var command = engine.Tick(Observation(offer) with { Tier = 0 }, Missing, Now)!;
        engine.Dispatched(command, new(false, true), Now);
        AssertPhase(PurchasePhase.Blocked, engine);
        Assert.Null(engine.Tick(Observation(offer, 400, 1), Missing, Now));
        Assert.Equal(0, engine.ConfirmedCount);
    }

    [Fact]
    public void QueueSnapshotRetainsBardingNativeIdentityAndAnchorDimensions()
    {
        var offer = Offer() with { Category = ShopCategory.Barding, BardingUnlockId = 42, SheetRowId = 77, SheetSubrowId = 3, SortKey = 5 };
        var snapshot = Assert.Single(CatalogClassification.Snapshot([offer]));
        Assert.Equal(42u, snapshot.BardingUnlockId);
        Assert.Equal(77u, snapshot.SheetRowId);
        Assert.Equal((ushort)3, snapshot.SheetSubrowId);
        Assert.Equal((byte)5, snapshot.SortKey);
        var anchor = new ShopAnchor(100, 200, 400, 600);
        Assert.Equal((100f, 200f, 400f, 600f), (anchor.X, anchor.Y, anchor.Width, anchor.Height));
    }
}
