using BetterGlamorPlates.Adapters;
using BetterGlamorPlates.Bootstrap;
using BetterGlamorPlates.Features.GlamourPlates;

namespace BetterGlamorPlates.Tests.Bootstrap;

/// <summary>Verifies catalog publication and failure side effects through the production coordinator.</summary>
public sealed class IconCatalogPublicationTests
{
    /// <summary>Incremental work remains unpublished until completion, and subsequent updates do nothing.</summary>
    [Fact]
    public void PublishesOnlyCompletedCatalogOnce()
    {
        var complete = false;
        var advances = 0;
        var reads = 0;
        var publications = new List<IReadOnlyList<GameIconOption>>();
        IReadOnlyList<GameIconOption> icons = [new(7, "Icon")];
        var coordinator = new IconCatalogPublication(() => complete, () => complete = ++advances == 2,
            () => throw new InvalidOperationException("Unexpected disposal"), () => { reads++; return icons; },
            publications.Add, _ => throw new InvalidOperationException("Unexpected failure"));

        coordinator.Update();
        Assert.Empty(publications);
        Assert.Equal(0, reads);
        coordinator.Update();
        coordinator.Update();

        Assert.Equal(2, advances);
        Assert.Equal(1, reads);
        Assert.Same(icons, Assert.Single(publications));
    }

    /// <summary>A failed real catalog releases its enumerator, reports once and publishes its unavailable empty result.</summary>
    [Fact]
    public void EnumerationFailureReleasesBeforeReportingAndPublishesEmptyReadyStateOnce()
    {
        var failure = new InvalidOperationException("Bad sheet row");
        var events = new List<string>();
        IEnumerable<GameIconFact> Rows()
        {
            try
            {
                yield return new(7, "Valid partial row", false, false, false);
                throw failure;
            }
            finally { events.Add("released"); }
        }
        using var catalog = new GameIconCatalogState(() => Rows().GetEnumerator());
        IReadOnlyList<GameIconOption>? published = null;
        var unavailable = false;
        var ready = false;
        var coordinator = new IconCatalogPublication(() => catalog.Complete, catalog.Advance, catalog.Dispose, () => catalog.Icons,
            icons => { events.Add("published"); published = icons; ready = true; },
            error => { Assert.Same(failure, error); events.Add("reported"); unavailable = true; });

        coordinator.Update();
        coordinator.Update();

        Assert.True(catalog.Complete);
        Assert.True(unavailable);
        Assert.True(ready);
        Assert.NotNull(published);
        Assert.Empty(published);
        Assert.Equal("released,reported,published", string.Join(',', events));
    }

    /// <summary>A catalog already complete before an update is not enumerated or republished.</summary>
    [Fact]
    public void AlreadyCompletedCatalogDoesNotTouchAnySideEffect()
    {
        var calls = 0;
        var coordinator = new IconCatalogPublication(() => true, () => calls++, () => calls++,
            () => { calls++; return []; }, _ => calls++, _ => calls++);

        coordinator.Update();

        Assert.Equal(0, calls);
    }

    /// <summary>Publication failures remain visible to the owner and do not masquerade as enumeration failures.</summary>
    [Fact]
    public void PublicationFailurePropagatesWithoutCleanupOrRetry()
    {
        var complete = false;
        var failure = new InvalidOperationException("View failed");
        var cleanup = 0;
        var reports = 0;
        var publications = 0;
        var coordinator = new IconCatalogPublication(() => complete, () => complete = true, () => cleanup++, () => [],
            _ => { publications++; throw failure; }, _ => reports++);

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(coordinator.Update));
        coordinator.Update();

        Assert.Equal(1, publications);
        Assert.Equal(0, cleanup);
        Assert.Equal(0, reports);
    }
}
