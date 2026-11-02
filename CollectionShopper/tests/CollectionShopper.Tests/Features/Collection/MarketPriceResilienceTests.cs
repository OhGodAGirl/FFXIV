using System.Net;
using System.Text;

namespace CollectionShopper.Tests;

public sealed class MarketPriceResilienceTests
{
    [Fact]
    public void MergeKeepsOnlyFailedPreviousQuotesAndReplacesSuccessfulQuotes()
    {
        var oldQuote = new MarketPriceQuote(new(1, 100, 1, "Example", false), null);
        var newQuote = new MarketPriceQuote(new(3, 200, 1, "Example", false), null);
        var result = new MarketPriceRefreshResult(new Dictionary<uint, MarketPriceQuote> { [3] = newQuote }, [1]);
        var previous = new Dictionary<uint, MarketPriceQuote> { [1] = oldQuote, [2] = oldQuote, [3] = oldQuote };
        var merged = result.MergePrevious(previous);
        Assert.Equal(2, merged.Count);
        Assert.Same(oldQuote, merged[1]);
        Assert.Same(newQuote, merged[3]);
        Assert.False(merged.ContainsKey(2));
        Assert.Equal(3, previous.Count);
        Assert.Throws<ArgumentNullException>(() => result.MergePrevious(null!));
        Assert.Single(result.MergePrevious(new Dictionary<uint, MarketPriceQuote>()));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task TransientReadFailureRetriesAndKeepsSuccessfulBatch(HttpStatusCode status)
    {
        var failedAttempts = 0;
        var delays = new List<TimeSpan>();
        using var client = new FfxivCollectClient(new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/101", StringComparison.Ordinal))
            {
                failedAttempts++;
                return new(status);
            }
            return Json("""{"results":[{"itemId":1,"nq":{"minListing":{"world":{"price":123}}}}]}""");
        })), (delay, _) => { delays.Add(delay); return Task.CompletedTask; });

        var result = await client.GetPriceResultsAsync("Example", Enumerable.Range(1, 101).Select(i => (uint)i).ToArray(), CancellationToken.None);

        Assert.Equal(3, failedAttempts);
        Assert.Equal(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }, delays);
        Assert.Equal(1U, Assert.Single(result.Prices).Key);
        Assert.Equal(Enumerable.Range(2, 100).Select(i => (uint)i), result.FailedItemIds.Order());
    }

    [Fact]
    public async Task RecoveredReadPublishesPricesWithoutFailedItems()
    {
        var attempts = 0;
        using var client = new FfxivCollectClient(new HttpClient(new Handler(_ => ++attempts == 1
            ? new(HttpStatusCode.GatewayTimeout)
            : Json("""{"results":[{"itemId":42,"nq":{"minListing":{"world":{"price":123}}}}]}"""))),
            (_, _) => Task.CompletedTask);
        var result = await client.GetPriceResultsAsync("Example", [42], CancellationToken.None);
        Assert.Equal(2, attempts);
        Assert.Single(result.Prices);
        Assert.Empty(result.FailedItemIds);
    }

    [Fact]
    public async Task ApiFailedItemsRemainUnavailableAndUnrequestedQuotesAreIgnored()
    {
        using var client = new FfxivCollectClient(new HttpClient(new Handler(_ => Json("""
            {"results":[{"itemId":99,"nq":{"minListing":{"world":{"price":123}}}}],"failedItems":[42,99]}
            """))));
        var result = await client.GetPriceResultsAsync("Example", [42, 43], CancellationToken.None);
        Assert.Empty(result.Prices);
        Assert.Equal(new uint[] { 42, 43 }, result.FailedItemIds.Order());
    }

    [Theory]
    [InlineData("{\"itemId\":42}")]
    [InlineData("{\"itemId\":42,\"nq\":null}")]
    [InlineData("{\"itemId\":42,\"nq\":{\"minListing\":[]}}")]
    [InlineData("{\"itemId\":42,\"nq\":{\"minListing\":{\"world\":{\"price\":\"100\"}}}}")]
    public async Task MalformedRequestedResultRetainsPreviousQuote(string item)
    {
        using var client = new FfxivCollectClient(new HttpClient(new Handler(_ => Json("{\"results\":[" + item + "]}"))));
        var result = await client.GetPriceResultsAsync("Example", [42], CancellationToken.None);
        var quote = new MarketPriceQuote(new(42, 100, 1, "Example", false), null);
        Assert.Equal(42U, Assert.Single(result.FailedItemIds));
        Assert.Same(quote, result.MergePrevious(new Dictionary<uint, MarketPriceQuote> { [42] = quote })[42]);
    }

    [Fact]
    public async Task OmittedRequestedItemRetainsQuoteButExplicitNoListingClearsIt()
    {
        using var client = new FfxivCollectClient(new HttpClient(new Handler(_ => Json("""
            {"results":[{"itemId":43,"nq":{"minListing":{}},"hq":{"minListing":{"world":null}}}]}
            """))));
        var result = await client.GetPriceResultsAsync("Example", [42, 43], CancellationToken.None);
        var quote = new MarketPriceQuote(new(42, 100, 1, "Example", false), null);
        var merged = result.MergePrevious(new Dictionary<uint, MarketPriceQuote> { [42] = quote, [43] = quote });
        Assert.Equal(42U, Assert.Single(result.FailedItemIds));
        Assert.Equal(42U, Assert.Single(merged).Key);
        Assert.Empty(result.Prices);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"results\":null}")]
    public async Task MalformedBatchIsUnavailableInsteadOfNoListings(string payload)
    {
        using var client = new FfxivCollectClient(new HttpClient(new Handler(_ => Json(payload))));
        var result = await client.GetPriceResultsAsync("Example", [42], CancellationToken.None);
        Assert.Empty(result.Prices);
        Assert.Equal(42U, Assert.Single(result.FailedItemIds));
    }

    [Fact]
    public async Task NonTransientFailureIsNotRetried()
    {
        var attempts = 0;
        using var client = new FfxivCollectClient(new HttpClient(new Handler(_ =>
        {
            attempts++;
            return new(HttpStatusCode.BadRequest);
        })), (_, _) => throw new InvalidOperationException("Retry was not expected."));
        var result = await client.GetPriceResultsAsync("Example", [42], CancellationToken.None);
        Assert.Equal(1, attempts);
        Assert.Equal(42U, Assert.Single(result.FailedItemIds));
    }

    [Fact]
    public async Task CancellationDuringRetryRemainsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var client = new FfxivCollectClient(new HttpClient(new Handler(_ => new(HttpStatusCode.GatewayTimeout))),
            (_, token) => { cancellation.Cancel(); return Task.FromCanceled(token); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetPriceResultsAsync("Example", [42], cancellation.Token));
    }

    private static HttpResponseMessage Json(string payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(payload, Encoding.UTF8, "application/json"),
    };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref requests) > 20)
                throw new InvalidOperationException("The client exceeded the fixture request budget.");
            return Task.FromResult(response(request));
        }

        private int requests;
    }
}
