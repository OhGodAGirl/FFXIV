using System.Reflection;
using EZDelivery.Features.ExpertDelivery;
using EZDelivery.Features.Shopping;
using Xunit;

// Fault injection exercises retained corruption guards. These states are not
// reachable through ordinary public transitions; public behavior tests remain separate.
namespace EZDelivery.Tests.ExpertDelivery
{
    public sealed partial class DeliverySafetyTests
    {
        [Fact]
        public void CorruptedMissingPendingDeliveryStopsWithoutAnotherNativeCall()
        {
            var (native, engine) = Start();
            typeof(ExpertDeliveryFacade).GetField("pending", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(engine, null);
            engine.Tick(2);
            Assert.Equal(DeliveryReason.MappingMismatch, engine.Status.Reason);
            Assert.False(engine.IsRunning);
            Assert.Single(native.Calls);
        }
    }
}

namespace EZDelivery.Tests.Shopping
{
    public sealed partial class PurchaseEngineTests
    {
        [Fact]
        public void CorruptedQueuedCommandKindIsRejectedWithoutConfirmingPurchase()
        {
            var engine = new PurchaseEngine();
            var offer = Offer();
            engine.Start([offer], Identity);
            var command = engine.Tick(Observation(offer), Missing, Now)!;
            var corrupt = command with { Kind = (ShopCommandKind)int.MaxValue };
            typeof(PurchaseEngine).GetField("awaitingDispatch", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(engine, corrupt);
            Assert.Throws<ArgumentOutOfRangeException>(() => engine.Dispatched(corrupt, new(true, true), Now));
            Assert.Equal(0, engine.ConfirmedCount);
            Assert.Empty(engine.Suppressed);
        }
    }
}
