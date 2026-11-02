using EZDelivery.Features.Shopping;

namespace EZDelivery.Tests.Shopping;

public sealed partial class PurchaseEngineTests
{
    [Fact]
    public void RepeatedCancellationAndDuplicatePurchaseReceiptsEmitOneAcquisition()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var offer = Offer();
            var engine = ReadyToReconcile(offer, Offer(2));
            var acquisitions = 0;
            engine.Acquired += _ => acquisitions++;
            engine.Stop();
            engine.Stop();
            for (var duplicate = 0; duplicate < 10; duplicate++)
                Assert.Null(engine.Tick(Observation(offer, 400, 1), Missing, Now.AddMilliseconds(duplicate)));
            Assert.Equal(1, acquisitions);
            Assert.Equal(1, engine.ConfirmedCount);
            Assert.False(engine.IsRunning);
            Assert.Null(engine.Tick(Observation(Offer(2)), Missing, Now.AddSeconds(10)));
        }
    }

    [Fact]
    public void RepeatedSessionReplacementRejectsStaleDispatchAndClearsOnlyPriorCharacterState()
    {
        var engine = new PurchaseEngine();
        ShopCommand? stale = null;
        for (var iteration = 1; iteration <= 100; iteration++)
        {
            var identity = Identity with { Character = (ulong)iteration };
            var offer = Offer();
            engine.ClearCharacter(identity.Character);
            Assert.True(engine.Start([offer], identity));
            var observation = Observation(offer) with { Identity = identity };
            var command = engine.Tick(observation, Missing, Now)!;
            if (stale is not null)
            {
                engine.Dispatched(stale, new(true, true), Now);
                Assert.Equal(PurchasePhase.ValidateNext, engine.Phase);
                Assert.Null(engine.Tick(observation, Missing, Now));
            }
            engine.Dispatched(command, new(false, false), Now);
            Assert.Equal(PurchasePhase.Blocked, engine.Phase);
            Assert.Empty(engine.Suppressed);
            stale = command;
        }
    }
}
