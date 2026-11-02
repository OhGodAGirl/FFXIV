using BetterGlamorPlates.Bootstrap;

namespace BetterGlamorPlates.Tests.Bootstrap;

/// <summary>Verifies optional configuration polling has bounded retries and retains the last palette.</summary>
public sealed class ThemeObserverTests
{
    /// <summary>Only changed available themes publish at the exact one-second boundary.</summary>
    [Fact]
    public void PollingIsBoundedAndOnlyChangedThemesPublish()
    {
        uint? value = 2;
        var reads = 0;
        var themes = new List<uint>();
        var observer = new ThemeObserver(() => { reads++; return value; }, themes.Add);
        observer.Update(0);
        value = 3;
        observer.Update(999);
        Assert.Equal([2u], themes);
        Assert.Equal(1, reads);
        observer.Update(1000);
        observer.Update(2000);
        value = null;
        observer.Update(3000);
        value = 2;
        observer.Update(4000);
        Assert.Equal([2u, 3u, 2u], themes);
        Assert.Equal(5, reads);
    }

    /// <summary>Read and publication errors are retried only at the next poll boundary.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedObservationRetriesAtNextInterval(bool publishFailure)
    {
        var fail = true;
        var reads = 0;
        var published = new List<uint>();
        var observer = new ThemeObserver(() =>
        {
            reads++;
            if (fail && !publishFailure) throw new InvalidOperationException();
            return 2;
        }, theme =>
        {
            if (fail) throw new InvalidOperationException();
            published.Add(theme);
        });
        observer.Update(0);
        fail = false;
        observer.Update(999);
        Assert.Empty(published);
        observer.Update(1000);
        Assert.Equal([2u], published);
        Assert.Equal(2, reads);
    }
}
