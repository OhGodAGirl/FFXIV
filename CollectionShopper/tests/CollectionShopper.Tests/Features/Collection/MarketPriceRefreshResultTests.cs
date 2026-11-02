namespace CollectionShopper.Tests;

public sealed class MarketPriceRefreshResultTests
{
    [Fact]
    public void MissingPreviousQuotesNamesTheBoundaryInput()
    {
        var result = new MarketPriceRefreshResult(new Dictionary<uint, MarketPriceQuote>(), []);

        var error = Assert.Throws<ArgumentNullException>(() => result.MergePrevious(null!));

        Assert.Equal("previous", error.ParamName);
    }

    [Fact]
    public void EmptySuccessfulRefreshClearsPreviousQuotesWithoutChangingTheInput()
    {
        var old = Quote(1, 100);
        var previous = new Dictionary<uint, MarketPriceQuote> { [1] = old };
        var result = new MarketPriceRefreshResult(new Dictionary<uint, MarketPriceQuote>(), []);

        var merged = result.MergePrevious(previous);

        Assert.Empty(merged);
        Assert.NotSame(previous, merged);
        Assert.Equal(1U, Assert.Single(previous).Key);
        Assert.Same(old, previous[1]);
    }

    [Fact]
    public void EntirelyFailedRefreshRetainsKnownQuotesWithoutInventingMissingPrices()
    {
        var first = Quote(1, 100);
        var second = Quote(2, 200);
        var previous = new Dictionary<uint, MarketPriceQuote> { [1] = first, [2] = second };
        var result = new MarketPriceRefreshResult(new Dictionary<uint, MarketPriceQuote>(), [1, 2, 99]);

        var merged = result.MergePrevious(previous);

        Assert.Equal(2, merged.Count);
        Assert.Same(first, merged[1]);
        Assert.Same(second, merged[2]);
        Assert.False(merged.ContainsKey(99));
        Assert.NotSame(previous, merged);
        Assert.Same(first, previous[1]);
        Assert.Same(second, previous[2]);
    }

    [Fact]
    public void CurrentQuoteTakesPrecedenceOverFailedHistoryForTheSameItem()
    {
        var old = Quote(1, 100);
        var current = Quote(1, 125);
        var previous = new Dictionary<uint, MarketPriceQuote> { [1] = old };
        var incoming = new Dictionary<uint, MarketPriceQuote> { [1] = current };
        var result = new MarketPriceRefreshResult(incoming, [1]);

        var merged = result.MergePrevious(previous);

        Assert.Same(current, Assert.Single(merged).Value);
        Assert.Same(old, previous[1]);
        Assert.Same(current, incoming[1]);
    }

    [Fact]
    public void GeneratedMergesPreserveFailureMeaningCurrentPrecedenceAndInputValues()
    {
        var random = new Random(20260914);
        for (var iteration = 0; iteration < 128; iteration++)
        {
            var ids = Enumerable.Range(1, 32).Select(id => (uint)id).ToArray();
            var previous = ids.Where(_ => random.Next(3) != 0).ToDictionary(id => id, id => Quote(id, id * 10));
            var current = ids.Where(_ => random.Next(3) == 0).ToDictionary(id => id, id => Quote(id, id * 10 + 1));
            var failed = ids.Where(_ => random.Next(2) == 0).ToHashSet();
            var before = previous.ToArray();
            var currentBefore = current.ToArray();

            var merged = new MarketPriceRefreshResult(current, failed).MergePrevious(previous);

            Assert.All(ids, id =>
            {
                if (current.TryGetValue(id, out var fresh))
                    Assert.Same(fresh, merged[id]);
                else if (failed.Contains(id) && previous.TryGetValue(id, out var old))
                    Assert.Same(old, merged[id]);
                else
                    Assert.False(merged.ContainsKey(id));
            });
            Assert.Equal(current.Keys.Union(previous.Keys.Intersect(failed)).Count(), merged.Count);
            Assert.Equal(before, previous.ToArray());
            Assert.Equal(currentBefore, current.ToArray());
        }
    }

    private static MarketPriceQuote Quote(uint itemId, uint price)
        => new(new(itemId, price, 1, "Example", false), null);
}
