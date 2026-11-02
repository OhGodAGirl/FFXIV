using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using CollectionShopper;

const int sampleCount = 60;
const int maximumWarmupOperations = 1_000_000;
var cpuWarmupDuration = TimeSpan.FromSeconds(2);
const int operationsPerSample = 8;

var outputArgument = Array.IndexOf(args, "--output");
var outputPath = outputArgument >= 0 && outputArgument + 1 < args.Length
    ? Path.GetFullPath(args[outputArgument + 1])
    : Path.GetFullPath(Path.Combine("artifacts", "performance", "latest.json"));

var shoppingItems = Enumerable.Range(1, 2_048)
    .Select(index => new ShoppingItem(
        (uint)index,
        $"Fixture item {index:D4}",
        (uint)(index / 4),
        $"Fixture collection {index % 37:D2}",
        null,
        "7.0",
        "Fixture source",
        (ushort)(index % ushort.MaxValue),
        true,
        index % 11 == 0,
        new MarketPriceQuote(
            new MarketPrice((uint)index, (uint)(1_000 + index), 1, "Fixture", false),
            new MarketPrice((uint)index, (uint)(2_000 + index), 1, "Fixture", true)),
        (ShoppingCategory)(index % Enum.GetValues<ShoppingCategory>().Length),
        index % 13,
        index % 8))
    .ToArray();
var completedIds = shoppingItems
    .Where((_, index) => index % 7 == 0)
    .Select(item => item.ItemId)
    .ToHashSet();

var clippingOcclusions = Enumerable.Range(0, 32)
    .Select(index => new MarketBoardTabPresentation.ClipRegion(
        new Vector2(20 + index % 4 * 17, index / 4 * 100),
        new Vector2(29 + index % 4 * 17, index / 4 * 100 + 45)))
    .ToArray();
var visibleTabRegions = new List<MarketBoardTabPresentation.ClipRegion>(4);
var tabRegionScratch = new List<MarketBoardTabPresentation.ClipRegion>(4);
var clippedRegionCount = 0;

var listings = Enumerable.Range(0, 100)
    .Select(index => new PurchaseListingCandidate(
        index,
        42,
        (ulong)(10_000 + index),
        (uint)(10_000 - index),
        (uint)(index % 23),
        1,
        index % 3 == 0,
        false))
    .ToArray();

var priceItemIds = Enumerable.Range(1, 2_500).Select(value => (uint)value).ToArray();
using var serialPriceClient = new FfxivCollectClient(
    new HttpClient(new DelayedUniversalisHandler(maximumConcurrency: 1)));
using var boundedPriceClient = new FfxivCollectClient(
    new HttpClient(new DelayedUniversalisHandler(maximumConcurrency: 8)));

var scenarios = new List<Measurement>
{
    Measure(
        "bookmark-clipping-10-tabs-32-occluders-allocating-baseline",
        () =>
        {
            var count = 0;
            for (var tab = 0; tab < 10; tab++)
                count += MarketBoardTabPresentation.GetVisibleRegions(
                    new Vector2(0, tab * 89), new Vector2(98, 74), clippingOcclusions).Count;
            clippedRegionCount = count;
        }),
    Measure(
        "bookmark-clipping-10-tabs-32-occluders-reused",
        () =>
        {
            var count = 0;
            for (var tab = 0; tab < 10; tab++)
            {
                MarketBoardTabPresentation.FillVisibleRegions(
                    new Vector2(0, tab * 89), new Vector2(98, 74),
                    clippingOcclusions, visibleTabRegions, tabRegionScratch);
                count += visibleTabRegions.Count;
            }
            clippedRegionCount = count;
        }),
    Measure(
        "shopping-projection-2048-items",
        () =>
        {
            _ = ShoppingProjection.CountOwnershipStates(
                shoppingItems,
                ShopperPage.AllMissing,
                completedIds);
            _ = ShoppingProjection.BuildVisible(
                shoppingItems,
                ShopperPage.AllMissing,
                "fixture",
                hideLocallyOwned: true,
                hideCompleted: true,
                completedIds,
                currentItemId: 1,
                allowHighQuality: false);
        }),
    Measure(
        "purchase-policy-100-listings",
        () => _ = PurchasePolicy.SelectBestListing(listings, 42, allowHighQuality: false)),
    MeasureNetwork(
        "universalis-pricing-2500-items-serial-baseline",
        () => serialPriceClient.GetPricesAsync("Fixture", priceItemIds, CancellationToken.None)
            .GetAwaiter()
            .GetResult()),
    MeasureNetwork(
        "universalis-pricing-2500-items-bounded",
        () => boundedPriceClient.GetPricesAsync("Fixture", priceItemIds, CancellationToken.None)
            .GetAwaiter()
            .GetResult()),
};
GC.KeepAlive(clippedRegionCount);

var report = new
{
    schemaVersion = 1,
    measuredUtc = DateTimeOffset.UtcNow,
    runtime = Environment.Version.ToString(),
    operatingSystem = Environment.OSVersion.ToString(),
    processorCount = Environment.ProcessorCount,
    samplesPerScenario = sampleCount,
    operationsPerSample,
    cpuWarmupSeconds = cpuWarmupDuration.TotalSeconds,
    maximumWarmupOperations,
    measurementScope = "Warm-path CPU latency; process CPU includes runtime workers; GC deltas are process-wide. No live game timing claim.",
    scenarios,
};

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
await File.WriteAllTextAsync(
    outputPath,
    JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(outputPath);
foreach (var scenario in scenarios)
{
    Console.WriteLine(
        $"{scenario.Name}: mean {scenario.MeanMilliseconds:F6} ms, "
        + $"p95 {scenario.P95Milliseconds:F6} ms, "
        + $"{scenario.MeanAllocatedBytes:F2} B/op");
}

Measurement Measure(string name, Action action)
{
    // Exercise the intended warm path long enough for runtime optimization, with a
    // time and operation bound. The previous ten calls did not establish steady state.
    var warmupStarted = Stopwatch.GetTimestamp();
    var warmupOperations = 0;
    while (warmupOperations < maximumWarmupOperations
        && Stopwatch.GetElapsedTime(warmupStarted) < cpuWarmupDuration)
    {
        action();
        warmupOperations++;
    }
    var warmupMilliseconds = Stopwatch.GetElapsedTime(warmupStarted).TotalMilliseconds;

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var milliseconds = new double[sampleCount];
    var allocations = new double[sampleCount];
    var rawSamples = new Sample[sampleCount];
    using var process = Process.GetCurrentProcess();
    for (var sample = 0; sample < sampleCount; sample++)
    {
        process.Refresh();
        var cpuBefore = process.TotalProcessorTime;
        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var operation = 0; operation < operationsPerSample; operation++)
            action();
        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

        milliseconds[sample] = elapsed.TotalMilliseconds / operationsPerSample;
        allocations[sample] = (allocatedAfter - allocatedBefore) / (double)operationsPerSample;
        var gen0Collections = GC.CollectionCount(0) - gen0Before;
        var gen1Collections = GC.CollectionCount(1) - gen1Before;
        var gen2Collections = GC.CollectionCount(2) - gen2Before;
        process.Refresh();
        rawSamples[sample] = new Sample(sample, milliseconds[sample], allocations[sample],
            (process.TotalProcessorTime - cpuBefore).TotalMilliseconds / operationsPerSample,
            gen0Collections, gen1Collections, gen2Collections);
    }

    Array.Sort(milliseconds);
    Array.Sort(allocations);
    var p95Index = (int)Math.Ceiling(sampleCount * 0.95) - 1;
    return new Measurement(
        name,
        sampleCount,
        milliseconds.Average(),
        milliseconds[p95Index],
        allocations.Average(),
        allocations[p95Index],
        warmupOperations,
        warmupMilliseconds,
        rawSamples);
}

Measurement MeasureNetwork(string name, Action action)
{
    const int networkSamples = 30;
    const int networkWarmups = 3;
    var warmupStarted = Stopwatch.GetTimestamp();
    for (var index = 0; index < networkWarmups; index++)
        action();
    var warmupMilliseconds = Stopwatch.GetElapsedTime(warmupStarted).TotalMilliseconds;

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var milliseconds = new double[networkSamples];
    var allocations = new double[networkSamples];
    var rawSamples = new Sample[networkSamples];
    using var process = Process.GetCurrentProcess();
    for (var sample = 0; sample < networkSamples; sample++)
    {
        process.Refresh();
        var cpuBefore = process.TotalProcessorTime;
        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var started = Stopwatch.GetTimestamp();
        action();
        milliseconds[sample] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        allocations[sample] = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var gen0Collections = GC.CollectionCount(0) - gen0Before;
        var gen1Collections = GC.CollectionCount(1) - gen1Before;
        var gen2Collections = GC.CollectionCount(2) - gen2Before;
        process.Refresh();
        rawSamples[sample] = new Sample(sample, milliseconds[sample], allocations[sample],
            (process.TotalProcessorTime - cpuBefore).TotalMilliseconds,
            gen0Collections, gen1Collections, gen2Collections);
    }

    Array.Sort(milliseconds);
    Array.Sort(allocations);
    var p95Index = (int)Math.Ceiling(networkSamples * 0.95) - 1;
    return new Measurement(
        name,
        networkSamples,
        milliseconds.Average(),
        milliseconds[p95Index],
        allocations.Average(),
        allocations[p95Index],
        networkWarmups,
        warmupMilliseconds,
        rawSamples);
}

internal sealed class DelayedUniversalisHandler(int maximumConcurrency) : HttpMessageHandler
{
    private static readonly TimeSpan ResponseDelay = TimeSpan.FromMilliseconds(3);
    private readonly SemaphoreSlim concurrency = new(maximumConcurrency);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Delay(ResponseDelay, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent("{\"items\":{}}"),
            };
        }
        finally
        {
            concurrency.Release();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            concurrency.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed record Measurement(
    string Name,
    int Samples,
    double MeanMilliseconds,
    double P95Milliseconds,
    double MeanAllocatedBytes,
    double P95AllocatedBytes,
    int WarmupOperations,
    double WarmupMilliseconds,
    IReadOnlyList<Sample> RawSamples);

internal sealed record Sample(
    int Index,
    double MillisecondsPerOperation,
    double AllocatedBytesPerOperation,
    double ProcessCpuMillisecondsPerOperation,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections);
