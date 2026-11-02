using EZDelivery.Features.Shopping;

namespace EZDelivery.Tests.Shopping;

public sealed class ShoppingFacadeLifecycleRigorTests
{
    [Fact]
    public void RepeatedCharacterLifetimesForwardOneReceiptAndReleasePriorSuppression()
    {
        var native = new Runtime();
        var now = DateTimeOffset.UnixEpoch;
        var facade = new ShoppingFacade(new Catalog(), native, _ => new(PurchasePresence.Missing), () => now);
        var receipts = 0;
        facade.Acquired += _ => receipts++;
        for (var iteration = 1; iteration <= 100; iteration++)
        {
            now = DateTimeOffset.UnixEpoch.AddSeconds(iteration);
            native.Value = new(true, "", new((ulong)iteration, 10, 1, 8, 20), 0, 0, 0, 1, 100, 500, 0, true);
            facade.Tick();
            Assert.Empty(facade.Suppressed);
            Assert.True(facade.BuyOne("one"));
            facade.Tick();
            Assert.Equal(iteration, native.Commands);
            native.Value = native.Value with { Seals = 400, ItemCount = 1 };
            facade.Tick();
            facade.Tick();
            Assert.Equal(iteration, receipts);
            Assert.Equal(1, facade.ConfirmedCount);
            Assert.Contains(1u, facade.Suppressed);
        }
    }

    private sealed class Catalog : IShopCatalog
    {
        public IReadOnlyList<ShopOffer> Offers { get; } = [new("one", 1, 10, "Item", ShopCategory.Weapons, 1, 1, 0, 0, 100, 0, 1, 0, 0)];
        public string Status => "Ready";
    }

    private sealed class Runtime : IShoppingRuntime
    {
        public PurchaseObservation Value = new(false, "", default, 0, 0, 0, 0, 0, 0, 0, false);
        public int Commands;
        public ShopAnchor? Anchor => null;
        public string LastDialogCheck => "None";
        public PurchaseObservation Observe(ShopOffer? pending) => Value;
        public ShopDispatchResult Dispatch(ShopCommand command)
        {
            Assert.Equal(ShopCommandKind.RequestOne, command.Kind);
            Commands++;
            return new(true, true, ExpectsConfirmation: false);
        }
    }
}
