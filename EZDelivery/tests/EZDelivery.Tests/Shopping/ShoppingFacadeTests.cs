using System.Reflection;
using EZDelivery.Features.Shopping;
using Xunit;

namespace EZDelivery.Tests.Shopping;

public sealed class ShoppingFacadeTests
{
    private static readonly ShopIdentity Identity = new(1, 10, 1, 8, 20);
    private static ShopOffer Offer(uint id = 1, byte company = 1, byte rank = 1)
        => new($"item:{id}", id, 10, "Item", ShopCategory.Weapons, company, rank, 0, 0, 100, 0, id, 0, 0);
    private sealed class Catalog(params ShopOffer[] offers) : IShopCatalog
    {
        public IReadOnlyList<ShopOffer> Offers => offers;
        public string Status => "Catalog ready";
    }
    private sealed class Native : IShoppingRuntime
    {
        public PurchaseObservation Value = new(true, "", Identity, 0, 0, 0, 1, 100, 500, 0, true);
        public List<ShopCommand> Commands { get; } = [];
        public string LastDialogCheck => "Checked";
        public ShopAnchor? Anchor => new(1, 2, 3, 4);
        public PurchaseObservation Observe(ShopOffer? pending) => Value;
        public ShopDispatchResult Dispatch(ShopCommand command) { Commands.Add(command); return new(true, true, ExpectsConfirmation: false); }
    }
    private static ShoppingFacade Create(Native native, Func<ShopOffer, PurchaseKnowledge>? knowledge = null, params ShopOffer[] offers)
        => new(new Catalog(offers.Length == 0 ? [Offer()] : offers), native,
            knowledge ?? (_ => new(PurchasePresence.Missing)), () => DateTimeOffset.UnixEpoch);

    [Fact]
    public void BeforeFirstObservationShoppingIsUnavailable()
    {
        var native = new Native();
        var facade = Create(native);
        Assert.Equal(new PurchaseObservation(false, "Not ready", default, -1, -1, -1, 0, 0, 0, -1, false), facade.Observation);
        Assert.Empty(facade.Offers);
        Assert.False(facade.IsRunning);
        Assert.Equal(0, facade.ConfirmedCount);
        Assert.False(facade.BuyOne("item:1"));
        Assert.Empty(native.Commands);
    }

    [Fact]
    public void VisibleCatalogUsesCurrentCompanyAndRankAndClearsOnLogout()
    {
        var native = new Native();
        var facade = Create(native, null, Offer(), Offer(2, 2), Offer(3, 1, 9), Offer(4, 1, 8));
        Assert.Equal("Catalog ready", facade.Status);
        facade.Tick();
        Assert.Equal(new[] { "item:1", "item:4" }, facade.Offers.Select(x => x.OfferKey));
        Assert.Equal(native.Anchor, facade.Anchor);
        Assert.Equal("Checked", facade.LastDialogCheck);
        Assert.Same(native.Value, facade.Observation);
        native.Value = native.Value with { Identity = Identity with { Character = 0 } };
        facade.Tick();
        Assert.Empty(facade.Offers);
        Assert.Empty(native.Commands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PurchaseReceiptIsForwardedOnceAndSuppressionClearsAfterDelivery(bool subscriber)
    {
        var native = new Native(); var facade = Create(native);
        var acquired = new List<ShopOffer>();
        if (subscriber) facade.Acquired += acquired.Add;
        facade.Tick();
        Assert.True(facade.BuyOne("item:1"));
        Assert.True(facade.IsRunning);
        Assert.False(facade.ResetPurchaseTracking());
        Assert.False(facade.BuyAll(["item:1"]));
        facade.Tick();
        Assert.Equal(ShopCommandKind.RequestOne, Assert.Single(native.Commands).Kind);
        Assert.Equal("Checking inventory and seals", facade.Status);
        facade.Tick();
        Assert.Single(native.Commands);
        native.Value = native.Value with { Seals = 400, ItemCount = 1 };
        facade.Tick(); facade.Tick();
        Assert.Equal(1, facade.ConfirmedCount);
        Assert.Equal(subscriber ? 1 : 0, acquired.Count);
        Assert.Contains(1u, facade.Suppressed);
        Assert.False(facade.BuyOne("item:1"));
        facade.ConfirmDelivery(Identity.Character, 1);
        Assert.Empty(facade.Suppressed);
        Assert.True(facade.ResetPurchaseTracking());
    }

    [Theory]
    [InlineData("absent", PurchasePresence.Missing)]
    [InlineData("item:1", PurchasePresence.Present)]
    [InlineData("item:1", PurchasePresence.Unknown)]
    public void UnknownOrNonMissingOffersNeverStart(string key, PurchasePresence presence)
    {
        var native = new Native(); var facade = Create(native, _ => new(presence));
        facade.Tick();
        Assert.False(facade.BuyOne(key));
        Assert.Empty(native.Commands);
    }

    [Fact]
    public void LogoutClearsPriorCharacterPurchaseSuppression()
    {
        var native = new Native(); var facade = Create(native);
        facade.Tick();
        Assert.True(facade.BuyOne("item:1"));
        facade.Tick();
        native.Value = native.Value with { Seals = 400, ItemCount = 1 };
        facade.Tick();
        Assert.Contains(1u, facade.Suppressed);
        native.Value = native.Value with { Identity = Identity with { Character = 0 } };
        facade.Tick();
        Assert.Empty(facade.Suppressed);
        Assert.Empty(facade.Offers);
        Assert.Single(native.Commands);
    }

    [Fact]
    public void OversizedQueueRejectsAndEmptyQueueDoesNotStart()
    {
        var native = new Native();
        var facade = Create(native, null, Enumerable.Range(1, 2049).Select(x => Offer((uint)x)).ToArray());
        facade.Tick();
        Assert.False(facade.BuyAll(facade.Offers.Select(x => x.OfferKey)));
        Assert.False(facade.BuyAll([]));
        Assert.Empty(native.Commands);
    }

    [Fact]
    public void StopBeforeDispatchLeavesNativeUntouched()
    {
        var native = new Native(); var facade = Create(native);
        facade.Tick(); facade.BuyOne("item:1"); facade.Stop(); facade.Tick();
        Assert.False(facade.IsRunning);
        Assert.Empty(native.Commands);
    }

    [Fact]
    public void CorruptQueuePositionCannotDispatchWithoutPendingOffer()
    {
        var native = new Native(); var facade = Create(native);
        facade.Tick(); facade.BuyOne("item:1");
        var engine = (PurchaseEngine)typeof(ShoppingFacade).GetField("engine", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(facade)!;
        typeof(PurchaseEngine).GetField("index", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(engine, 1);
        facade.Tick();
        Assert.Empty(native.Commands);
    }
}
