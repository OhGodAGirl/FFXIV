using Exportaramus.Features.CollectionData.Capture;

namespace Exportaramus.Tests.Capture;

public sealed class BeastReadinessTests
{
    [Fact]
    public void ReceivedEmptyAndNewUnlocksRequestCaptureWithoutDuplicateHints()
    {
        var policy = new BeastReadinessPolicy();
        Assert.False(policy.Poll(true, true, () => (false, 0), 0));
        Assert.True(policy.Poll(true, true, () => (true, 0), 1));
        Assert.False(policy.Poll(true, true, () => (true, 0), 2));
        Assert.True(policy.Poll(true, true, () => (true, 1), 3));
        Assert.False(policy.Poll(true, true, () => (true, 1), 4));
        Assert.False(policy.Poll(true, true, () => (false, 1), 5));
        Assert.True(policy.Poll(true, true, () => (true, 1), 6));
    }

    [Fact]
    public void IdentityCompatibilityAndThrottlePreventNativeReadsAndResetEdges()
    {
        var policy = new BeastReadinessPolicy();
        (bool, int) Forbidden() => throw new InvalidOperationException("Must not read.");
        int reads = 0;
        (bool, int) Read() { reads++; return (true, 1); }
        Assert.False(policy.Poll(false, true, Read, 0));
        Assert.False(policy.Poll(true, false, Read, 0));
        Assert.Equal(0, reads);
        Assert.True(policy.Poll(true, true, Read, 0));
        Assert.False(policy.Poll(true, true, Read, 0.5));
        Assert.Equal(1, reads);
        Assert.False(policy.Poll(false, true, Forbidden, 0.6));
        Assert.True(policy.Poll(true, true, Read, 0.6));
        Assert.False(policy.Poll(true, false, Forbidden, 0.7));
        Assert.True(policy.Poll(true, true, Read, 0.7));
        policy.Reset();
        Assert.True(policy.Poll(true, true, Read, 0.8));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(51)]
    public void InvalidCountsAndReadFailuresDoNotHintAndRecoveryDoes(int invalidCount)
    {
        var policy = new BeastReadinessPolicy();
        Assert.False(policy.Poll(true, true, () => (true, invalidCount), 0));
        Assert.True(policy.Poll(true, true, () => (true, 50), 1));
        Assert.False(policy.Poll(true, true, () => throw new IOException(), 2));
        Assert.True(policy.Poll(true, true, () => (true, 50), 3));
    }
}
