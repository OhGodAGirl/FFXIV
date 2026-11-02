using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SpringCleaning;
using SpringCleaning.Shared.CollectionData;

const int sampleCount = 60;
const int operationsPerSample = 100;
var root = FindPluginRoot();
var outputPath = args.Length == 0
    ? Path.Combine(root, "tests", "TestResults", "performance", "latest.json")
    : Path.GetFullPath(args[0]);

var eligible = Enumerable.Range(0, 800)
    .Select(index => new EligibleDresserItem(
        index,
        (uint)index,
        $"Entry {index}",
        Enumerable.Range(0, (index % 11) + 1)
            .Select(slot => new EligibleDresserComponent(
                slot,
                (uint)((index * 20) + slot),
                false,
                1,
                "Piece"))
            .ToArray()))
    .ToArray();
var outfits = Enumerable.Range(0, 40)
    .Select(index => new InventoryDresserOutfit(
        (uint)index,
        null,
        $"Outfit {index}",
        Enumerable.Range(0, 11)
            .Select(slot => new InventoryDresserComponent(
                0,
                slot,
                slot,
                (uint)((index * 20) + slot),
                false,
                "Piece",
                0))
            .ToArray()))
    .ToArray();
var items = Enumerable.Range(0, 140)
    .Select(index => new InventoryDresserItem(0, index, (uint)index, false, "Item", 0))
    .ToArray();
var duplicates = Enumerable.Range(0, 140)
    .Select(index => new InventoryDuplicateItem(
        0,
        index,
        (uint)index,
        false,
        "Duplicate",
        0,
        new InventoryItemSafety(true, index % 3 != 0, index % 3 != 0 ? "Ready" : "Blocked"),
        false,
        true,
        InventoryDuplicateDisposition.RemovalReview))
    .ToArray();
var reviewAlerts = Enumerable.Range(0, 800)
    .Select(index => new DresserAlertEntry(
        -1,
        (uint)index,
        (uint)index,
        false,
        false,
        1,
        "Gear",
        "Review item",
        0,
        DresserAlertKind.NotInOutfit,
        false,
        false,
        index % 3 == 0))
    .ToArray();
var previewNow = new DateTime(2026, 9, 1, 0, 0, 1, DateTimeKind.Utc);
var previewDue = previewNow.AddMilliseconds(-1);
var railOcclusions = new[]
{
    new NativeWindowOcclusionPolicy.Region(new Vector2(0, 40), new Vector2(82, 130)),
    new NativeWindowOcclusionPolicy.Region(new Vector2(20, 210), new Vector2(100, 300)),
    new NativeWindowOcclusionPolicy.Region(new Vector2(-30, 340), new Vector2(40, 440)),
};
var ownershipItems = Enumerable.Range(1, 20_000)
    .Select(value => (uint)value)
    .ToImmutableArray();
var emptyOwnership = new CollectionSourceSnapshot(
    CollectionSourceStatus.Fresh,
    DateTimeOffset.UtcNow,
    ImmutableArray<uint>.Empty,
    null);
var fullOwnership = emptyOwnership with { ItemIds = ownershipItems };
var ownershipSnapshot = new CollectionDataSnapshot(
    "CollectionLedgerHelper",
    "1.0.0.0",
    42,
    1,
    DateTimeOffset.UtcNow,
    fullOwnership,
    fullOwnership,
    emptyOwnership,
    emptyOwnership,
    emptyOwnership,
    emptyOwnership,
    emptyOwnership);

var restorationEntries = Enumerable.Range(1, 800).Select(index => new DresserAlertEntry(
    index - 1, (uint)index, (uint)index, false, false, 1, "Gear", "Item", 0,
    DresserAlertKind.ArmoireEligible, true, false, false)).ToArray();
var restorationSnapshot = ownershipSnapshot with
{
    Armoire = fullOwnership,
    DresserEntries = restorationEntries.Select(entry => new CollectionDresserEntry(
        entry.PrismBoxIndex, entry.RawItemId, entry.ItemId, null, [])).ToImmutableArray(),
};
var restoredAfter = duplicates.Select(copy => copy with { Safety = new(true, true, "Ready") }).ToArray();
var restoredBefore = restoredAfter.Take(70).ToArray();
var restoredExpected = restoredAfter.Skip(70).Select(copy => copy.ItemId).ToArray();
var drawerInventory = Enumerable.Range(1, 1024)
    .Select(index => duplicates[index % duplicates.Length] with { ItemId = (uint)index }).ToArray();
var drawerEntries = restorationEntries.Select(entry => entry with
{
    IsOutfit = entry.PrismBoxIndex % 2 == 0,
    Components = [new(0, entry.ItemId), new(1, entry.ItemId + 1)],
}).ToArray();
var ignoredDrawerIds = Enumerable.Range(1, 100).Select(id => (uint)(id * 5)).ToHashSet();
var drawerCache = new DuplicateReviewCache();
var qualityCandidates = Enumerable.Range(0, 140)
    .Select(index => new InventoryDresserCandidate(
        new InventoryDresserComponent(0, index, index % 5, (uint)(index + 1), index % 2 == 0, "Piece", 0),
        [(uint)(index / 5 + 1), (uint)(index / 5 + 101)]))
    .ToArray();
var qualityDestinations = Enumerable.Range(0, 800)
    .Select(index => (SetId: (uint)(index / 2 + 1), IsHighQuality: index % 2 == 0))
    .ToArray();
var checksum = 0L;
var scenarios = new[]
{
    Measure("Migration batch planning at 800 entries", () =>
    {
        checksum += MigrationBatchPlanner.GetBatchPieceCount(eligible, 140);
    }),
    Measure("Prism budget planning at 40 outfits and 140 items", () =>
    {
        checksum += DresserPrismBudgetPlanner.Plan(outfits, items, 400).SelectedPieces;
    }),
    Measure("Duplicate safety filtering at 140 inventory entries", () =>
    {
        checksum += InventoryDuplicatePlanner.SelectBulkDiscardCandidates(duplicates).Length;
    }),
    Measure("Review filtering at 800 Dresser entries", () =>
    {
        checksum += ReviewPlanner.Plan(
            duplicates,
            reviewAlerts,
            onlyItemsOutsideOutfits: true).Count;
    }),
    Measure("Visible-window preview refresh update decision", () =>
    {
        checksum += PreviewRefreshPolicy.ShouldRefresh(
            previewNow,
            previewDue,
            previewVisible: true,
            operationActive: false) ? 1 : 0;
    }),
    Measure("Collection ownership planning at 20,000 items per source", () =>
    {
        checksum += CollectionOwnershipPolicy.CanPlanDresserStorage(
            ownershipSnapshot,
            CollectionSourceName.Inventory,
            19_999,
            isArmoireEligible: false) ? 1 : 0;
    }),
    Measure("Five-tab native-window occlusion clipping", () =>
    {
        for (var tab = 0; tab < 5; tab++)
        {
            var minimum = new Vector2(0, tab * 82);
            checksum += NativeWindowOcclusionPolicy.GetVisibleRegions(
                new NativeWindowOcclusionPolicy.Region(
                    minimum,
                    minimum + new Vector2(82, 74)),
                railOcclusions).Count;
        }
    }),
    Measure("Stored restoration eligibility at 800 entries", () =>
    {
        foreach (var entry in restorationEntries)
            checksum += DresserRestorationPolicy.CanPlan(restorationSnapshot, entry) ? 1 : 0;
    }),
    Measure("Restored-copy attribution across 140 slots", () =>
    {
        checksum += RestoredCopyPolicy.Observe(restoredBefore, restoredAfter, restoredExpected).Copies.Count;
    }),
    Measure("Uncached duplicate drawer filtering at 1024 copies and 800 entries", () =>
    {
        var all = ReviewPlanner.Plan(drawerInventory, drawerEntries, false);
        var visible = new ReviewPlan(
            all.InventoryDuplicates.Where(copy => !ignoredDrawerIds.Contains(copy.ItemId)).ToArray(),
            all.DresserEntries.Where(entry => !ignoredDrawerIds.Contains(entry.ItemId)
                && !DresserRestorationPolicy.Components(entry).Any(ignoredDrawerIds.Contains)).ToArray());
        checksum += visible.Count;
    }),
    Measure("Cached duplicate drawer filtering at 1024 copies and 800 entries", () =>
    {
        checksum += drawerCache.Get(drawerInventory, drawerEntries, ignoredDrawerIds).Visible.Count;
    }),
    Measure("Duplicate drawer cache refresh at 1024 copies and 800 entries", () =>
    {
        drawerCache.Clear();
        checksum += drawerCache.Get(drawerInventory, drawerEntries, ignoredDrawerIds).Visible.Count;
    }),
    Measure("HQ/NQ outfit batching at 140 candidates and 800 destinations", () =>
    {
        checksum += InventoryOutfitSetPlanner.BuildBatches(qualityCandidates, qualityDestinations).Count;
    }),
};

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
var result = new
{
    measuredAtUtc = DateTimeOffset.UtcNow,
    sourceState = ComputeSourceState(root),
    runtime = Environment.Version.ToString(),
    sampleCount,
    operationsPerSample,
    checksum,
    scenarios,
};
File.WriteAllText(
    outputPath,
    JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
    Encoding.UTF8);
Console.WriteLine(outputPath);

ScenarioResult Measure(string name, Action operation)
{
    for (var warmup = 0; warmup < 20; warmup++)
        operation();

    var durations = new double[sampleCount];
    var allocations = new double[sampleCount];
    for (var sample = 0; sample < sampleCount; sample++)
    {
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var operationIndex = 0; operationIndex < operationsPerSample; operationIndex++)
            operation();
        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        durations[sample] = elapsed.TotalMilliseconds / operationsPerSample;
        allocations[sample] = (double)allocated / operationsPerSample;
    }

    Array.Sort(durations);
    Array.Sort(allocations);
    return new ScenarioResult(
        name,
        durations.Average(),
        Percentile(durations, 0.95),
        allocations.Average(),
        Percentile(allocations, 0.95));
}

static double Percentile(double[] sorted, double percentile)
{
    var index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
    return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
}

static string ComputeSourceState(string pluginRoot)
{
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    foreach (var path in Directory.EnumerateFiles(
                 Path.Combine(pluginRoot, "src"),
                 "*.cs",
                 SearchOption.AllDirectories)
             .Where(path => !Path.GetRelativePath(pluginRoot, path).Replace('\\', '/').StartsWith("src/Contracts/", StringComparison.Ordinal))
             .OrderBy(path => path, StringComparer.Ordinal))
    {
        var relative = Path.GetRelativePath(pluginRoot, path).Replace('\\', '/');
        hash.AppendData(Encoding.UTF8.GetBytes(relative));
        hash.AppendData(File.ReadAllBytes(path));
    }

    return Convert.ToHexString(hash.GetHashAndReset());
}

static string FindPluginRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props"))
            && Directory.Exists(Path.Combine(current.FullName, "src")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate the Spring Cleaning root.");
}

internal sealed record ScenarioResult(
    string Name,
    double MeanMilliseconds,
    double P95Milliseconds,
    double MeanAllocatedBytesPerOperation,
    double P95AllocatedBytesPerOperation);
