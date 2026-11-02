using EZDelivery.Features.ExpertDelivery;

namespace EZDelivery.Tests.ExpertDelivery;

public sealed partial class DeliverySafetyTests
{
    [Fact]
    public void RepeatedCancellationAndDuplicateReceiptsNeverSelectAnotherSource()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var (native, engine) = Start();
            var confirmed = 0;
            engine.Delivered += (_, _, _) => confirmed++;
            Reward(native);
            engine.Tick(10);
            engine.Stop();
            engine.Stop();
            native.Value = native.Value with { Seals = 45000, PendingSource = null };
            for (var duplicate = 0; duplicate < 10; duplicate++) engine.Tick(20 + duplicate);
            Assert.Equal(1, confirmed);
            Assert.Equal(1, engine.Status.ConfirmedCount);
            Assert.False(engine.IsRunning);
            Assert.Equal(new[] { DeliveryCommandKind.Select, DeliveryCommandKind.Submit }, native.Calls.Select(call => call.Kind));
        }
    }

    [Fact]
    public void GeneratedSealCapacityMatchesWiderArithmeticAtExtremes()
    {
        var random = new Random(20260915);
        uint[] boundaries = [0, 1, 44999, 45000, uint.MaxValue - 1, uint.MaxValue];
        foreach (var balance in boundaries)
        foreach (var cap in boundaries)
        foreach (var reward in boundaries)
            Assert.Equal(reward > 0 && (ulong)balance + reward <= cap, DeliveryRules.RewardFits(balance, cap, reward));
        for (var sample = 0; sample < 4000; sample++)
        {
            var balance = (uint)random.NextInt64(0, (long)uint.MaxValue + 1);
            var cap = (uint)random.NextInt64(0, (long)uint.MaxValue + 1);
            var reward = (uint)random.NextInt64(0, (long)uint.MaxValue + 1);
            Assert.Equal(reward > 0 && (ulong)balance + reward <= cap, DeliveryRules.RewardFits(balance, cap, reward));
        }
    }
}
