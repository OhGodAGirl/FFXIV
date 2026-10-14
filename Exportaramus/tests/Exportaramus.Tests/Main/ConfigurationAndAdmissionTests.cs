using Exportaramus.Bootstrap;
using Exportaramus.Features.Main;

namespace Exportaramus.Tests.Main;

public sealed class ConfigurationAndAdmissionTests
{
    [Theory]
    [InlineData(1, true, true)]
    [InlineData(1, false, false)]
    [InlineData(0, true, false)]
    [InlineData(2, true, false)]
    [InlineData(-1, true, false)]
    public void OnlyRecognizedConfigurationVersionRestoresThePlayerSetting(int version, bool stored, bool expected) =>
        Assert.Equal(expected, ConfigurationPolicy.RestoreReduceMotion(version, stored));

    [Theory]
    [InlineData(IntentAdmissionPolicy.MainCapacity, 16)]
    [InlineData(IntentAdmissionPolicy.DiagnosticsCapacity, 8)]
    public void IntentQueueAcceptsExactlyItsBoundWithoutDroppingOrReorderingAcceptedWork(int capacity, int expectedBound)
    {
        Assert.Equal(expectedBound, capacity);
        var queue = new Queue<int>();
        for (var i = 0; i < expectedBound; i++) Assert.True(IntentAdmissionPolicy.TryEnqueue(queue, i, capacity));
        Assert.False(IntentAdmissionPolicy.TryEnqueue(queue, 999, capacity));
        Assert.Equal(Enumerable.Range(0, expectedBound), queue);
        Assert.Equal(0, queue.Dequeue()); Assert.True(IntentAdmissionPolicy.TryEnqueue(queue, expectedBound, capacity));
        Assert.Equal(Enumerable.Range(1, expectedBound), queue);
    }
}
