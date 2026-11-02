using EZDelivery.Bootstrap;

namespace EZDelivery.Tests.Interface;

public sealed class CleanupActionsTests
{
    [Fact]
    public void ReleaseFailureDoesNotSkipAnyRemainingOwnerAndPreservesEveryError()
    {
        var first = new InvalidOperationException("First release");
        var second = new ArgumentException("Second release");
        var released = new List<string>();
        var error = Assert.Throws<AggregateException>(() => CleanupActions.Run(
            () => { released.Add("events"); throw first; },
            () => released.Add("command"),
            () => { released.Add("renderer"); throw second; },
            () => released.Add("native")));
        Assert.Equal(new[] { "events", "command", "renderer", "native" }, released);
        Assert.Equal(new Exception[] { first, second }, error.InnerExceptions);
    }

    [Fact]
    public void RepeatedIndependentLifetimesReleaseEveryOwnerExactlyOnce()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var releases = new int[5];
            CleanupActions.Run(releases.Select((_, index) => (Action)(() => releases[index]++)).ToArray());
            Assert.All(releases, count => Assert.Equal(1, count));
        }
        CleanupActions.Run();
    }
}
