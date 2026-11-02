using BetterGlamorPlates.Shared.Operations;

namespace BetterGlamorPlates.Tests;

/// <summary>Checks partial initialization and complete reverse-order release of UI resource pairs.</summary>
public sealed class OwnedResourcePairTests
{
    /// <summary>Both successful resources remain available and release once in reverse creation order.</summary>
    [Fact]
    public void ReleasesInReverseOrderExactlyOnce()
    {
        var calls = new List<int>();
        var first = new Resource(() => calls.Add(1));
        var second = new Resource(() => calls.Add(2));
        var pair = new OwnedResourcePair<Resource>(() => first, () => second);
        Assert.Same(first, pair.First);
        Assert.Same(second, pair.Second);
        pair.Dispose();
        pair.Dispose();
        Assert.Equal([2, 1], calls);
    }

    /// <summary>A first acquisition failure prevents second acquisition.</summary>
    [Fact]
    public void FirstCreationFailureDoesNotAttemptSecond()
    {
        var failure = new InvalidOperationException("first creation");
        var secondCalls = 0;
        var observed = Assert.Throws<InvalidOperationException>(() => new OwnedResourcePair<Resource>(() => throw failure,
            () => { secondCalls++; return new(() => { }); }));
        Assert.Same(failure, observed);
        Assert.Equal(0, secondCalls);
    }

    /// <summary>A failed second acquisition releases the first and preserves the construction failure.</summary>
    [Fact]
    public void SecondCreationFailureReleasesFirst()
    {
        var releases = 0;
        var failure = new InvalidOperationException("second creation");
        var observed = Assert.Throws<InvalidOperationException>(() => new OwnedResourcePair<Resource>(
            () => new(() => releases++), () => throw failure));
        Assert.Same(failure, observed);
        Assert.Equal(1, releases);
    }

    /// <summary>Cleanup failure does not hide the original second acquisition failure.</summary>
    [Fact]
    public void FailedPartialCleanupRetainsBothExceptions()
    {
        var creation = new InvalidOperationException("creation");
        var cleanup = new InvalidOperationException("cleanup");
        var observed = Assert.Throws<AggregateException>(() => new OwnedResourcePair<Resource>(
            () => new(() => throw cleanup), () => throw creation));
        Assert.Equal([creation, cleanup], observed.InnerExceptions);
    }

    /// <summary>A failing release cannot prevent the other release or enable a second disposal attempt.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DisposalAttemptsBothAndPreservesFailures(bool firstFails, bool secondFails)
    {
        var calls = new List<int>();
        var firstFailure = new InvalidOperationException("first release");
        var secondFailure = new InvalidOperationException("second release");
        var pair = new OwnedResourcePair<Resource>(
            () => new(() => { calls.Add(1); if (firstFails) throw firstFailure; }),
            () => new(() => { calls.Add(2); if (secondFails) throw secondFailure; }));
        var failure = Record.Exception(pair.Dispose);
        if (firstFails && secondFails)
            Assert.Equal([secondFailure, firstFailure], Assert.IsType<AggregateException>(failure).InnerExceptions);
        else Assert.Same(firstFails ? firstFailure : secondFailure, failure);
        pair.Dispose();
        Assert.Equal([2, 1], calls);
    }

    private sealed class Resource(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
