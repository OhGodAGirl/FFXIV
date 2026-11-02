using System.Net;
using System.Text;

namespace CollectionShopper.Tests;

public sealed class PriceResultBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransportCancellationIsUnavailableButCallerCancellationPropagates(bool callerCancelled)
    {
        using var cancellation = new CancellationTokenSource();
        using var client = new FfxivCollectClient(new HttpClient(new Handler((_, _) =>
        {
            if (callerCancelled) cancellation.Cancel();
            throw new OperationCanceledException("Transport stopped.");
        })));
        var operation = client.GetPriceResultsAsync("Example", [42], cancellation.Token);
        if (callerCancelled)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        else
        {
            var result = await operation;
            Assert.Empty(result.Prices);
            Assert.Equal([42u], result.FailedItemIds);
        }
    }

    [Theory]
    [InlineData("[42,\"43\",null,{},4294967296]", true)]
    [InlineData("{}", false)]
    [InlineData("null", false)]
    public async Task ExplicitFailureOverridesConflictingQuoteAndIgnoresMalformedFailureIds(string failed, bool rejectsQuote)
    {
        using var client = new FfxivCollectClient(new HttpClient(new Handler((_, _) => Task.FromResult(Json(
            "{\"results\":[{\"itemId\":42,\"hq\":{\"minListing\":{\"world\":{\"price\":123}}}}],\"failedItems\":" + failed + "}")))));
        var result = await client.GetPriceResultsAsync("Example", [42], CancellationToken.None);
        Assert.Equal(rejectsQuote, result.FailedItemIds.Contains(42u));
        Assert.Equal(!rejectsQuote, result.Prices.ContainsKey(42));
        if (!rejectsQuote) Assert.Equal(123u, result.Prices[42].HighQuality?.PricePerUnit);
    }

    [Fact]
    public async Task DuplicateQuoteIsUnavailableEvenWhenBothRowsAreValid()
    {
        const string row = "{\"itemId\":42,\"nq\":{\"minListing\":{\"world\":{\"price\":123}}}}";
        using var client = new FfxivCollectClient(new HttpClient(new Handler((_, _) => Task.FromResult(Json(
            "{\"results\":[" + row + "," + row + "]}")))));
        var result = await client.GetPriceResultsAsync("Example", [42], CancellationToken.None);
        Assert.Empty(result.Prices);
        Assert.Equal([42u], result.FailedItemIds);
    }

    [Fact]
    public async Task BoundedBatchesReleaseSlotsAndEventuallyCompleteEveryRequestedItem()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        using var client = new FfxivCollectClient(new HttpClient(new Handler(async (_, token) =>
        {
            Interlocked.Increment(ref count);
            await release.Task.WaitAsync(token).ConfigureAwait(false);
            return Json("{\"results\":[]}");
        })));
        using var cancellation = new CancellationTokenSource();
        var ids = Enumerable.Range(1, 501).Select(id => (uint)id).ToArray();
        var operation = client.GetPriceResultsAsync("Example", ids, cancellation.Token);
        try
        {
            Assert.Equal(4, Volatile.Read(ref count));
            release.SetResult();
            var result = await operation.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(6, count);
            Assert.Equal(ids, result.FailedItemIds.Order());
            Assert.Empty(result.Prices);
        }
        finally { cancellation.Cancel(); }
    }

    [Fact]
    public async Task OtherWorldAndHistoricalPricesDoNotBecomeCurrentWorldEstimates()
    {
        using var client = new FfxivCollectClient(new HttpClient(new Handler((_, _) => Task.FromResult(Json("""
            {"results":[
              {"itemId":3481,
               "nq":{"minListing":{"dc":{"price":25270,"worldId":62}},
                     "recentPurchase":{"world":{"price":25270}}},
               "hq":{"minListing":{"region":{"price":30000,"worldId":62}}}},
              {"itemId":3487,
               "nq":{"minListing":{"region":{"price":192999,"worldId":40}}},
               "hq":{"minListing":{"world":{"price":150000},"dc":{"price":20000,"worldId":37}}}}
            ],"failedItems":[]}
            """)))));

        var result = await client.GetPriceResultsAsync("Balmung", [3481, 3487], CancellationToken.None);

        Assert.Empty(result.FailedItemIds);
        Assert.False(result.Prices.ContainsKey(3481));
        var quote = Assert.Single(result.Prices).Value;
        Assert.Null(quote.NormalQuality);
        Assert.Equal(new MarketPrice(3487, 150000, 1, "Balmung", true), quote.HighQuality);
    }
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json"),
    };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        private int requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref requests) > 20)
                throw new InvalidOperationException("The client exceeded the fixture request budget.");
            return response(request, cancellationToken);
        }
    }
}
