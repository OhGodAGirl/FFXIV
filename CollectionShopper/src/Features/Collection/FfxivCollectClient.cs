using System.Net.Http.Headers;
using System.Text.Json;

namespace CollectionShopper;

internal sealed class FfxivCollectClient : IDisposable
{
    private const string CollectApi = "https://ffxivcollect.com/api";
    private const string UniversalisApi = "https://universalis.app/api/v2";
    private const int PriceBatchSize = 100;
    private const int MaximumConcurrentPriceRequests = 4;
    private const int MaximumCatalogItemsPerCategory = 5_000;
    private const int MaximumResponseBytes = 16 * 1024 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private readonly HttpClient httpClient;
    private readonly Func<TimeSpan, CancellationToken, Task> retryDelay;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    public FfxivCollectClient()
        : this(new HttpClient())
    {
    }

    internal FfxivCollectClient(HttpClient httpClient, Func<TimeSpan, CancellationToken, Task>? retryDelay = null)
    {
        this.httpClient = httpClient;
        this.retryDelay = retryDelay ?? Task.Delay;
        httpClient.Timeout = RequestTimeout;
        httpClient.MaxResponseContentBufferSize = MaximumResponseBytes;
        httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(
                "CollectionShopperDalamud",
                typeof(FfxivCollectClient).Assembly.GetName().Version!.ToString(3)));
    }

    public void Dispose() => httpClient.Dispose();

    public async Task<MarketPriceRefreshResult> GetPriceResultsAsync(
        string market, IReadOnlyCollection<uint> itemIds, CancellationToken cancellationToken)
    {
        using var concurrency = new SemaphoreSlim(MaximumConcurrentPriceRequests);
        var requests = itemIds.Distinct().Chunk(PriceBatchSize).Select(async batch =>
        {
            await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                for (var attempt = 0; ; attempt++)
                {
                    try
                    {
                        return await ReadPriceBatchAsync(market, batch, cancellationToken).ConfigureAwait(false);
                    }
                    catch (HttpRequestException exception) when (attempt < 2
                        && exception.StatusCode is System.Net.HttpStatusCode.BadGateway
                            or System.Net.HttpStatusCode.ServiceUnavailable
                            or System.Net.HttpStatusCode.GatewayTimeout
                            or System.Net.HttpStatusCode.TooManyRequests)
                    {
                        await retryDelay(TimeSpan.FromSeconds(attempt + 1), cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is HttpRequestException or JsonException
                        or InvalidDataException or IOException
                        || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                    {
                        return new MarketPriceRefreshResult(new Dictionary<uint, MarketPriceQuote>(), batch);
                    }
                }
            }
            finally
            {
                concurrency.Release();
            }
        });
        var results = await Task.WhenAll(requests).ConfigureAwait(false);
        return new(results.SelectMany(result => result.Prices).ToDictionary(pair => pair.Key, pair => pair.Value),
            results.SelectMany(result => result.FailedItemIds).Distinct().ToArray());
    }

    public async Task<CollectionCatalogResult> GetCollectionsAsync(
        CancellationToken cancellationToken)
    {
        var minionsTask = GetAsync<CollectionCatalogPage>($"{CollectApi}/minions", cancellationToken);
        var mountsTask = GetAsync<CollectionCatalogPage>($"{CollectApi}/mounts", cancellationToken);
        var orchestrionsTask = GetAsync<CollectionCatalogPage>($"{CollectApi}/orchestrions", cancellationToken);
        var hairstylesTask = GetAsync<CollectionCatalogPage>($"{CollectApi}/hairstyles", cancellationToken);
        var facewearTask = GetAsync<CollectionCatalogPage>($"{CollectApi}/facewear", cancellationToken);
        var fashionsTask = GetAsync<CollectionCatalogPage>($"{CollectApi}/fashions", cancellationToken);

        await Task.WhenAll(
            minionsTask,
            mountsTask,
            orchestrionsTask,
            hairstylesTask,
            facewearTask,
            fashionsTask).ConfigureAwait(false);

        return new CollectionCatalogResult(
            ValidateCatalog(await minionsTask),
            ValidateCatalog(await mountsTask),
            ValidateCatalog(await orchestrionsTask),
            ValidateCatalog(await hairstylesTask),
            ValidateCatalog(await facewearTask),
            ValidateCatalog(await fashionsTask));
    }

    public async Task<IReadOnlyDictionary<uint, MarketPriceQuote>> GetPricesAsync(
        string market,
        IReadOnlyCollection<uint> itemIds,
        CancellationToken cancellationToken)
    {
        var batches = itemIds.Distinct().Chunk(PriceBatchSize).ToArray();
        if (batches.Length == 0)
            return new Dictionary<uint, MarketPriceQuote>();

        using var concurrency = new SemaphoreSlim(MaximumConcurrentPriceRequests);
        var batchTasks = batches.Select(async batch =>
        {
            await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await GetPriceBatchAsync(market, batch, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                concurrency.Release();
            }
        });
        var batchPrices = await Task.WhenAll(batchTasks).ConfigureAwait(false);
        return batchPrices
            .SelectMany(batch => batch)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private async Task<IReadOnlyDictionary<uint, MarketPriceQuote>> GetPriceBatchAsync(
        string market,
        uint[] batch,
        CancellationToken cancellationToken)
        => (await ReadPriceBatchAsync(market, batch, cancellationToken).ConfigureAwait(false)).Prices;

    private async Task<MarketPriceRefreshResult> ReadPriceBatchAsync(
        string market,
        uint[] batch,
        CancellationToken cancellationToken)
    {
        var prices = new Dictionary<uint, MarketPriceQuote>();
        var ids = string.Join(',', batch);
        using var response = await httpClient.GetAsync(
            $"{UniversalisApi}/aggregated/{Uri.EscapeDataString(market)}/{ids}",
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array)
        {
            return new(prices, batch);
        }

        var receivedIds = new HashSet<uint>();
        var malformedIds = new HashSet<uint>();
        foreach (var item in results.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("itemId", out var id)
                && id.ValueKind == JsonValueKind.Number && id.TryGetUInt32(out var itemId) && batch.Contains(itemId))
            {
                if (!receivedIds.Add(itemId) || !HasValidPriceResult(item))
                {
                    malformedIds.Add(itemId);
                }
                else
                {
                    MarketPriceResponsePolicy.AddAggregatedMinimums(prices, market, item);
                }
            }
        }

        // Only a valid result establishes that a requested item has no listing.
        var failedIds = batch.Where(id => !receivedIds.Contains(id)).ToHashSet();
        failedIds.UnionWith(malformedIds);
        if (document.RootElement.TryGetProperty("failedItems", out var failed) && failed.ValueKind == JsonValueKind.Array)
        {
            foreach (var id in failed.EnumerateArray())
                if (id.ValueKind == JsonValueKind.Number && id.TryGetUInt32(out var itemId) && batch.Contains(itemId))
                    failedIds.Add(itemId);
        }
        foreach (var id in failedIds)
            prices.Remove(id);
        return new(prices, failedIds.ToArray());
    }

    private static bool HasValidPriceResult(JsonElement item)
    {
        var hasQuality = false;
        foreach (var name in new[] { "nq", "hq" })
        {
            if (!item.TryGetProperty(name, out var quality))
                continue;
            hasQuality = true;
            if (quality.ValueKind != JsonValueKind.Object
                || !quality.TryGetProperty("minListing", out var listing)
                || listing.ValueKind != JsonValueKind.Object)
                return false;
            if (!listing.TryGetProperty("world", out var world) || world.ValueKind == JsonValueKind.Null)
                continue;
            if (world.ValueKind != JsonValueKind.Object
                || !world.TryGetProperty("price", out var price)
                || price.ValueKind != JsonValueKind.Number
                || !price.TryGetUInt32(out var value) || value == 0)
                return false;
        }
        return hasQuality;
    }

    private async Task<T> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, jsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"The service returned no usable data for {url}.");
    }

    private static IReadOnlyList<Collectable> ValidateCatalog(CollectionCatalogPage page)
    {
        if (page.Count is < 0 or > MaximumCatalogItemsPerCategory
            || page.Results is null
            || page.Results.Count != page.Count
            || page.Results.Any(item => item is null
                || item.Id == 0
                || item.Name is null or { Length: 0 or > 160 }
                || item.Patch is null or { Length: > 16 }
                || item.Sources is null
                || item.Sources.Count > 64))
        {
            throw new InvalidDataException("FFXIV Collect returned an invalid or oversized public catalog.");
        }

        return page.Results;
    }


}
