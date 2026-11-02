using System.Net;
using System.Text;
using System.Text.Json;

namespace CollectionShopper.Tests;

public sealed class HttpMutationBoundaryTests
{
    private const string EmptyCatalog = "{\"count\":0,\"results\":[]}";
    private const string EmptyPrices = "{\"results\":[]}";

    [Fact]
    public async Task RequestsIdentifyThePlugin()
    {
        var headers = new List<string>();
        using var client = CreateClient(request =>
        {
            headers.Add(request.Headers.UserAgent.ToString());
            return Response(EmptyCatalog);
        });

        await client.GetCollectionsAsync(CancellationToken.None);

        Assert.Equal(6, headers.Count);
        Assert.All(headers, header => Assert.StartsWith("CollectionShopperDalamud/", header));
    }

    [Fact]
    public async Task FailedCatalogRefreshStillOwnsAllOutstandingRequests()
    {
        var pending = new List<TaskCompletionSource<HttpResponseMessage>>();
        using var client = new FfxivCollectClient(new HttpClient(new AsyncHandler((_, _) =>
        {
            if (pending.Count == 0)
            {
                pending.Add(new TaskCompletionSource<HttpResponseMessage>());
                return Task.FromException<HttpResponseMessage>(new HttpRequestException("Catalog unavailable"));
            }

            var completion = new TaskCompletionSource<HttpResponseMessage>();
            pending.Add(completion);
            return completion.Task;
        })));

        var refresh = client.GetCollectionsAsync(CancellationToken.None);
        try
        {
            Assert.Equal(6, pending.Count);
            Assert.False(refresh.IsCompleted);
        }
        finally
        {
            foreach (var completion in pending.Skip(1))
                completion.TrySetResult(Response(EmptyCatalog));
        }

        await Assert.ThrowsAsync<HttpRequestException>(() => refresh);
    }

    [Fact]
    public async Task CompletedPriceBatchesReleaseCapacityForRemainingBatches()
    {
        var requests = 0;
        using var cancellation = new CancellationTokenSource();
        using var client = CreateClient(_ =>
        {
            requests++;
            return Response(EmptyPrices);
        });

        // Synchronous responses make every admitted batch finish before the next one starts.
        // Cancellation drains a broken semaphore path without relying on a test timeout.
        var refresh = client.GetPricesAsync("Example", Enumerable.Range(1, 501).Select(id => (uint)id).ToArray(), cancellation.Token);
        var completed = refresh.IsCompletedSuccessfully;
        cancellation.Cancel();
        try
        {
            await refresh;
        }
        catch (OperationCanceledException)
        {
            Assert.True(completed, "Completed batches must release capacity for all six requests.");
        }

        Assert.True(completed);
        Assert.Equal(6, requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpFailureRejectsAnOtherwiseValidBody(bool prices)
    {
        using var client = CreateClient(_ => Response(prices ? EmptyPrices : EmptyCatalog, HttpStatusCode.ServiceUnavailable));

        var error = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            if (prices)
                await client.GetPricesAsync("Example", [1], CancellationToken.None);
            else
                await client.GetCollectionsAsync(CancellationToken.None);
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
    }

    [Theory]
    [InlineData(1, 1, 0, 0)]
    [InlineData(5000, 1, 0, 0)]
    [InlineData(1, 160, 16, 64)]
    public async Task CatalogAcceptsInclusiveDocumentedBounds(int count, int nameLength, int patchLength, int sources)
    {
        var item = Item(new string('n', nameLength), new string('p', patchLength), Enumerable.Repeat(new { type = "Quest", text = "Source" }, sources).ToArray());
        using var client = CreateClient(_ => Response(Catalog(Enumerable.Repeat(item, count).ToArray())));

        var result = await client.GetCollectionsAsync(CancellationToken.None);

        Assert.Equal(count, result.Minions.Count);
        Assert.Equal(nameLength, result.Minions[0].Name.Length);
        Assert.Equal(patchLength, result.Minions[0].Patch.Length);
        Assert.Equal(sources, result.Minions[0].Sources.Count);
    }

    public static IEnumerable<object[]> InvalidCatalogs()
    {
        yield return [Catalog([null])];
        yield return [Catalog([Item(null, "7.3", Array.Empty<object>())])];
        yield return [Catalog([Item("", "7.3", Array.Empty<object>())])];
        yield return [Catalog([Item(new string('n', 161), "7.3", Array.Empty<object>())])];
        yield return [Catalog([Item("Name", null, Array.Empty<object>())])];
        yield return [Catalog([Item("Name", new string('p', 17), Array.Empty<object>())])];
        yield return [Catalog([Item("Name", "7.3", null)])];
        yield return [Catalog([Item("Name", "7.3", Enumerable.Repeat(new { type = "Quest", text = "Source" }, 65).ToArray())])];
        yield return [Catalog(Enumerable.Repeat(Item("Name", "7.3", Array.Empty<object>()), 5001).ToArray())];
    }

    [Theory]
    [MemberData(nameof(InvalidCatalogs))]
    public async Task InvalidCatalogExplainsTheRejectedServiceData(string body)
    {
        using var client = CreateClient(_ => Response(body));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => client.GetCollectionsAsync(CancellationToken.None));

        Assert.Contains("FFXIV Collect", error.Message, StringComparison.Ordinal);
        Assert.Contains("catalog", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invalid", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseLargerThanSixteenMiBIsRejected(bool prices)
    {
        var body = (prices ? EmptyPrices : EmptyCatalog).PadRight((16 * 1024 * 1024) + 1);
        using var client = CreateClient(_ => Response(body));

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            if (prices)
                await client.GetPricesAsync("Example", [1], CancellationToken.None);
            else
                await client.GetCollectionsAsync(CancellationToken.None);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseAtSixteenMiBRemainsUsable(bool prices)
    {
        var body = (prices ? EmptyPrices : EmptyCatalog).PadRight(16 * 1024 * 1024);
        using var client = CreateClient(_ => Response(body));

        if (prices)
            Assert.Empty(await client.GetPricesAsync("Example", [1], CancellationToken.None));
        else
            Assert.Empty((await client.GetCollectionsAsync(CancellationToken.None)).Minions);
    }
    private static object Item(string? name, string? patch, object? sources)
        => new { id = 1, name, patch, sources };

    private static string Catalog(object?[] items)
        => JsonSerializer.Serialize(new { count = items.Length, results = items });

    private static FfxivCollectClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> response)
        => new(new HttpClient(new AsyncHandler((request, _) => Task.FromResult(response(request)))));

    private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class AsyncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}
