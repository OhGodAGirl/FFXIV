using EZDelivery.Features.ExpertDelivery;
using Xunit;

namespace EZDelivery.Tests.ExpertDelivery;

public sealed class NativeDeliveryPolicyTests
{
    [Fact]
    public void RewardCorrelationRequiresEveryIndependentIdentityCheck()
    {
        // Arrange: exercise all 64 combinations, including every isolated failure.
        for (var mask = 0; mask < 64; mask++)
        {
            var checks = Enumerable.Range(0, 6).Select(bit => (mask & (1 << bit)) != 0).ToArray();

            // Act
            var correlated = NativeDeliveryPolicy.RewardCorrelated(checks[0], checks[1], checks[2], checks[3], checks[4], checks[5]);

            // Assert
            Assert.Equal(mask == 63, correlated);
        }
    }

    [Fact]
    public void RewardCallbackRequiresPresentReceiverExpectedKindAndReviewedClass()
    {
        // Arrange
        ulong[] kinds = [0, 1, 2, 3, 4, ulong.MaxValue];
        foreach (var present in new[] { false, true })
        foreach (var receiverClass in new[] { false, true })
        foreach (var kind in kinds)
        {
            // Act
            var matches = NativeDeliveryPolicy.RewardCallbackMatches(present, kind, receiverClass);

            // Assert
            Assert.Equal(present && receiverClass && kind == 2, matches);
        }
    }

    [Theory]
    [InlineData(0ul, 0ul, true)]
    [InlineData(1ul, 1ul, true)]
    [InlineData(ulong.MaxValue, ulong.MaxValue, true)]
    [InlineData(0ul, 1ul, false)]
    [InlineData(1ul, 0ul, false)]
    [InlineData(1ul, ulong.MaxValue, false)]
    public void AnyBuffFingerprintChangeInvalidatesRewardRead(ulong selected, ulong current, bool expected)
    {
        // Arrange: fingerprints were captured at selection and reward observation.
        // Act
        var unchanged = NativeDeliveryPolicy.BuffsUnchanged(selected, current);

        // Assert
        Assert.Equal(expected, unchanged);
    }

    [Theory]
    [InlineData(0u, 0u, null)]
    [InlineData(1u, 0u, 1u)]
    [InlineData(0u, 1u, 1u)]
    [InlineData(1912u, 287u, 2199u)]
    [InlineData(uint.MaxValue, 0u, uint.MaxValue)]
    [InlineData(0u, uint.MaxValue, uint.MaxValue)]
    [InlineData(uint.MaxValue - 1, 1u, uint.MaxValue)]
    [InlineData(uint.MaxValue, 1u, null)]
    [InlineData(1u, uint.MaxValue, null)]
    [InlineData(uint.MaxValue, uint.MaxValue, null)]
    public void BonusInclusiveRewardRejectsZeroAndOverflow(uint baseReward, uint bonus, uint? expected)
    {
        // Arrange: both amounts have already passed guarded native unsigned-value reads.
        // Act
        var reward = NativeDeliveryPolicy.RewardAmount(baseReward, bonus);

        // Assert
        Assert.Equal(expected, reward);
    }
}
