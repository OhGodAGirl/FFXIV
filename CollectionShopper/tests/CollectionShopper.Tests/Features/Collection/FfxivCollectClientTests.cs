using System.Net;
using System.Text;

namespace CollectionShopper.Tests;

/// <summary>
/// Verifies tolerant Universalis response handling and request boundaries.
/// </summary>
public sealed class FfxivCollectClientTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"minListing\":null}")]
    [InlineData("{\"minListing\":[]}")]
    [InlineData("{\"minListing\":{\"world\":[]}}")]
    [InlineData("{\"minListing\":{\"world\":{}}}")]
    [InlineData("{\"minListing\":{\"world\":{\"price\":\"100\"}}}")]
    [InlineData("{\"minListing\":{\"world\":{\"price\":-1}}}")]
    [InlineData("{\"minListing\":{\"world\":{\"price\":4294967296}}}")]
    [InlineData("{\"minListing\":{\"world\":{\"price\":1.5}}}")]
    [InlineData("{\"minListing\":{\"world\":{\"price\":0}}}")]
    public async Task InvalidQualityDoesNotEstablishAnEmptyMarketOrDiscardValidSiblings(string quality)
    {
        using var client = CreateClient("{\"results\":[{\"itemId\":42,\"nq\":" + quality
            + "},{\"itemId\":43,\"nq\":{\"minListing\":{\"world\":{\"price\":100}}}}]}");

        var result = await client.GetPriceResultsAsync("Example", [42, 43], CancellationToken.None);

        Assert.Equal([42u], result.FailedItemIds);
        Assert.False(result.Prices.ContainsKey(42));
        Assert.True(result.Prices.ContainsKey(43));
    }

    /// <summary>
    /// Verifies the production constructor creates and owns its HTTP boundary.
    /// </summary>
    [Fact]
    public void DefaultConstructorCanBeDisposedWithoutNetworkAccess()
    {
        // Arrange

        // Act
        using var client = new FfxivCollectClient();

        // Assert
        Assert.NotNull(client);
    }

    /// <summary>
    /// Verifies every supported collection category is requested and mapped.
    /// </summary>
    [Fact]
    public async Task GetCollectionsAsyncRequestsPublicCatalogCategories()
    {
        // Arrange
        var handler = new RecordingHandler(request =>
        {
            return JsonResponse("""{"count":0,"results":[]}""", request);
        });
        using var client = new FfxivCollectClient(new HttpClient(handler));

        // Act
        var result = await client.GetCollectionsAsync(CancellationToken.None);

        // Assert
        Assert.Empty(result.Minions);
        Assert.Empty(result.Mounts);
        Assert.Empty(result.Orchestrions);
        Assert.Empty(result.Hairstyles);
        Assert.Empty(result.Facewear);
        Assert.Empty(result.Fashions);
        Assert.Equal(
            [
                "/api/facewear",
                "/api/fashions",
                "/api/hairstyles",
                "/api/minions",
                "/api/mounts",
                "/api/orchestrions",
            ],
            handler.RequestUris.Select(uri => uri.AbsolutePath).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(handler.RequestUris, uri => uri.AbsolutePath.Contains("/characters/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{\"count\":-1,\"results\":[]}")]
    [InlineData("{\"count\":5001,\"results\":[]}")]
    [InlineData("{\"count\":1,\"results\":[]}")]
    [InlineData("{\"count\":0,\"results\":null}")]
    [InlineData("{\"count\":1,\"results\":[{\"id\":0,\"name\":\"Invalid\",\"patch\":\"7.3\",\"sources\":[]}]}")]
    public async Task GetCollectionsAsyncRejectsInvalidCatalogs(string response)
    {
        using var client = CreateClient(response);

        var action = () => client.GetCollectionsAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidDataException>(action);
    }

    /// <summary>
    /// Verifies malformed aggregate entries are skipped without discarding valid NQ and HQ prices.
    /// </summary>
    [Fact]
    public async Task GetPricesAsyncSkipsMalformedListings()
    {
        // Arrange
        const string response = """
            {
              "results": [
                null,
                "invalid",
                { "itemId": null },
                {
                  "itemId": 42,
                  "nq": { "minListing": { "world": { "price": 100 } } },
                  "hq": { "minListing": { "world": { "price": 50 } } }
                },
                { "itemId": 43, "nq": { "minListing": { "world": { "price": null } } } }
              ]
            }
            """;
        using var client = CreateClient(response);

        // Act
        var prices = await client.GetPricesAsync("Example", [42], CancellationToken.None);

        // Assert
        var quote = Assert.Single(prices).Value;
        Assert.Equal(100U, quote.NormalQuality?.PricePerUnit);
        Assert.Equal(50U, quote.HighQuality?.PricePerUnit);
    }

    /// <summary>
    /// Verifies null and non-array result containers do not fail a batch.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"invalid\"")]
    public async Task GetPricesAsyncSkipsMalformedResultsContainer(string resultsJson)
    {
        // Arrange
        var response = $$"""
            {
              "results": {{resultsJson}}
            }
            """;
        using var client = CreateClient(response);

        // Act
        var prices = await client.GetPricesAsync("Example", [42, 43], CancellationToken.None);

        // Assert
        Assert.Empty(prices);
    }

    /// <summary>
    /// Verifies malformed individual aggregate payloads do not discard valid siblings.
    /// </summary>
    [Fact]
    public async Task GetPricesAsyncSkipsMalformedItemPayloads()
    {
        // Arrange
        const string response = """
            {
              "results": [
                {
                  "itemId": 42,
                  "nq": { "minListing": { "world": { "price": 125 } } }
                },
                null,
                "invalid"
              ]
            }
            """;
        using var client = CreateClient(response);

        // Act
        var prices = await client.GetPricesAsync("Example", [42, 43, 44], CancellationToken.None);

        // Assert
        var quote = Assert.Single(prices).Value;
        Assert.Equal(125U, quote.NormalQuality?.PricePerUnit);
    }

    /// <summary>
    /// Verifies the aggregated minimum for each quality is returned.
    /// </summary>
    [Fact]
    public async Task GetPricesAsyncReadsAggregatedMinimumsByQuality()
    {
        // Arrange
        const string response = """
            {
              "results": [
                {
                  "itemId": 42,
                  "nq": { "minListing": { "world": { "price": 200 } } },
                  "hq": { "minListing": { "world": { "price": 150 } } }
                }
              ]
            }
            """;
        using var client = CreateClient(response);

        // Act
        var prices = await client.GetPricesAsync("Example", [42], CancellationToken.None);

        // Assert
        var quote = Assert.Single(prices).Value;
        Assert.Equal(200U, quote.NormalQuality?.PricePerUnit);
        Assert.Equal(150U, quote.HighQuality?.PricePerUnit);
    }

    /// <summary>
    /// Verifies an empty request returns immediately without contacting Universalis.
    /// </summary>
    [Fact]
    public async Task GetPricesAsyncDoesNotSendRequestForEmptyItemSet()
    {
        // Arrange
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("No request expected."));
        using var client = new FfxivCollectClient(new HttpClient(handler));

        // Act
        var prices = await client.GetPricesAsync("Example", [], CancellationToken.None);

        // Assert
        Assert.Empty(prices);
        Assert.Empty(handler.RequestUris);
    }

    /// <summary>
    /// Verifies duplicate item IDs are removed before the 100-item request batches
    /// are formed.
    /// </summary>
    [Fact]
    public async Task GetPricesAsyncDeduplicatesAndBatchesItemIds()
    {
        // Arrange
        var handler = new RecordingHandler(request =>
        {
            return JsonResponse("{\"results\":[]}", request);
        });
        using var client = new FfxivCollectClient(new HttpClient(handler));
        var ids = Enumerable.Range(1, 101).Select(value => (uint)value).Append(1U).ToArray();

        // Act
        var prices = await client.GetPricesAsync("Example World", ids, CancellationToken.None);

        // Assert
        Assert.Empty(prices);
        Assert.Equal(2, handler.RequestUris.Length);
        Assert.Contains("Example%20World", handler.RequestUris[0].AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal(100, handler.RequestUris[0].AbsolutePath.Split('/').Last().Split(',').Length);
        Assert.Single(handler.RequestUris[1].AbsolutePath.Split('/').Last().Split(','));
    }

    /// <summary>
    /// Verifies large Universalis refreshes overlap requests without exceeding the
    /// client's bounded concurrency limit.
    /// </summary>
    [Fact]
    public async Task GetPricesAsyncUsesBoundedParallelBatches()
    {
        // Arrange
        var handler = new ConcurrencyRecordingHandler();
        using var client = new FfxivCollectClient(new HttpClient(handler));
        using var cancellation = new CancellationTokenSource();
        var ids = Enumerable.Range(1, 501).Select(value => (uint)value).ToArray();

        // Act
        var refresh = client.GetPricesAsync("Example", ids, cancellation.Token);
        var requestCount = handler.RequestCount;
        var maximumConcurrency = handler.MaximumConcurrency;
        cancellation.Cancel();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        Assert.Equal(4, requestCount);
        Assert.Equal(4, maximumConcurrency);
    }

    /// <summary>
    /// Verifies cancellation is passed through to the HTTP operation.
    /// </summary>
    [Fact]
    public async Task GetPricesAsyncPropagatesCancellation()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var client = new FfxivCollectClient(new HttpClient(new CancelledHandler()));

        // Act
        var action = () => client.GetPricesAsync("Example", [42], cancellation.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(action);
    }

    /// <summary>
    /// Verifies non-success HTTP responses remain visible to the caller.
    /// </summary>
    [Fact]
    public async Task GetPricesAsyncPropagatesHttpFailure()
    {
        // Arrange
        var handler = new RecordingHandler(request => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            RequestMessage = request,
        });
        using var client = new FfxivCollectClient(new HttpClient(handler));

        // Act
        var action = () => client.GetPricesAsync("Example", [42], CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<HttpRequestException>(action);
    }

    private static FfxivCollectClient CreateClient(string response)
        => new(new HttpClient(new RecordingHandler(request => JsonResponse(response, request))));

    private static HttpResponseMessage JsonResponse(string response, HttpRequestMessage request)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(response, Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        private readonly Lock sync = new();
        private readonly List<Uri> requestUris = [];

        public Uri[] RequestUris
        {
            get
            {
                lock (sync)
                    return requestUris.ToArray();
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            lock (sync)
                requestUris.Add(request.RequestUri ?? throw new InvalidOperationException("Request URI is required."));
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class CancelledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromCanceled<HttpResponseMessage>(cancellationToken);
    }

    private sealed class ConcurrencyRecordingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int activeRequests;
        private int maximumConcurrency;
        private int requestCount;

        internal int MaximumConcurrency => Volatile.Read(ref maximumConcurrency);

        internal int RequestCount => Volatile.Read(ref requestCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);
            var active = Interlocked.Increment(ref activeRequests);
            UpdateMaximum(active);
            try
            {
                await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return JsonResponse("{\"results\":[]}", request);
            }
            finally
            {
                Interlocked.Decrement(ref activeRequests);
            }
        }

        private void UpdateMaximum(int candidate)
        {
            var observed = Volatile.Read(ref maximumConcurrency);
            while (candidate > observed)
            {
                var replaced = Interlocked.CompareExchange(
                    ref maximumConcurrency,
                    candidate,
                    observed);
                if (replaced == observed)
                    return;
                observed = replaced;
            }
        }
    }
}
