namespace BetterGlamorPlates.Tests;

using System.Diagnostics;
using System.Globalization;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;
using BetterGlamorPlates.Shared.CollectionData;
using Xunit.Abstractions;

/// <summary>
/// Measures deterministic decision-path latency and allocation budgets without
/// touching Dalamud, ImGui, native state, files, or network services.
/// </summary>
[Collection("Performance measurements")]
public sealed class PerformanceBudgetTests
{
    private const int SampleCount = 64;
    private readonly ITestOutputHelper output;

    /// <summary>Initializes performance output capture.</summary>
    public PerformanceBudgetTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    /// <summary>Measures the maximum supported collection-view planning input.</summary>
    [Fact]
    [Trait("Category", "Performance")]
    public void CollectionViewPlanningStaysWithinBudget()
    {
        var itemIds = Enumerable.Range(1, CollectionViewPlanner.MaximumVisibleItems)
            .Select(index => (uint)index)
            .ToArray();
        var snapshot = TestDomainValues.SharedSnapshot(dresserItems: itemIds, armoireItems: []);
        var facts = itemIds.ToDictionary(
            itemId => itemId,
            itemId => new ItemDisplayFacts(
                $"Collected Item {CollectionViewPlanner.MaximumVisibleItems - (int)itemId:D4}",
                itemId,
                TestDomainValues.AllSlots));

        var result = Measure(
            "collection-view-2000",
            () => CollectionViewPlanner.Create(
                snapshot,
                PlateSlot.Body,
                "collected",
                itemId => facts[itemId]),
            iterationsPerSample: 8);

        Assert.True(result.P95Microseconds <= 5_000, result.ToString());
        Assert.True(result.MeanMicroseconds <= 4_000, result.ToString());
        Assert.True(result.AllocatedBytesPerOperation <= 400_000, result.ToString());
    }

    /// <summary>Measures exact-source validation of a fully populated plate.</summary>
    [Fact]
    [Trait("Category", "Performance")]
    public void CompletePlatePlanningStaysWithinBudget()
    {
        var collection = Enumerable.Range(1, 2_000)
            .Select(index => TestDomainValues.NativeItem((uint)index))
            .ToArray();
        var plate = TestDomainValues.Plate("Measured");
        for (var slot = 0; slot < PlateRules.SlotCount; slot++)
            plate.Items[slot] = new SavedPlateItem { ItemId = (uint)(1_900 + slot) };
        var snapshot = new NativeCollectionSnapshot(
            collection,
            true,
            true,
            TestDomainValues.Timestamp);

        var result = Measure(
            "plate-load-plan-12x2000",
            () => PlateLoadPlanner.Create(plate, snapshot, (_, _) => true),
            iterationsPerSample: 32);

        Assert.True(result.P95Microseconds <= 2_000, result.ToString());
        Assert.True(result.MeanMicroseconds <= 1_000, result.ToString());
        Assert.True(result.AllocatedBytesPerOperation <= 8_000, result.ToString());
    }

    /// <summary>Bounds a two-channel outfit against 140 inventory stacks shared by 24 colours.</summary>
    [Fact]
    [Trait("Category", "Performance")]
    public void DyeAllocationStaysWithinBudget()
    {
        var steps = Enumerable.Range(0, 12).Select(index => new PlateLoadStep((PlateSlot)index,
            new SavedPlateItem { ItemId = 100, Stain0Id = (byte)(index * 2 + 1), Stain1Id = (byte)(index * 2 + 2) },
            new NativeCollectionItem(100, CollectionSource.Dresser, 0, 0, 0))).ToArray();
        var plan = new PlateLoadPlan(steps, []);
        var supplies = Enumerable.Range(0, 140).SelectMany(index => Enumerable.Range(1, 24)
            .Select(dye => new DyeSupply((byte)dye, 52254, (uint)(index / 35), (ushort)(index % 35), index == 139 ? 24u : 0u))).ToArray();
        var result = Measure("plate-dyes-24x3360", () => PlateDyePlanner.Create(plan, supplies, _ => 2), 16);
        Assert.True(result.P95Microseconds <= 5_000, result.ToString());
        Assert.True(result.MeanMicroseconds <= 5_000, result.ToString());
        Assert.True(result.AllocatedBytesPerOperation <= 20_000, result.ToString());
    }

    /// <summary>Measures deep duplication of all twelve native slots.</summary>
    [Fact]
    [Trait("Category", "Performance")]
    public void FullPlateDuplicationStaysWithinBudget()
    {
        var plate = TestDomainValues.Plate("Measured");
        for (var slot = 0; slot < PlateRules.SlotCount; slot++)
        {
            plate.Items[slot] = new SavedPlateItem
            {
                ItemId = (uint)(slot + 1),
                Stain0Id = (byte)slot,
                Stain1Id = (byte)(slot + 1),
            };
        }

        var duplicateId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var result = Measure(
            "plate-duplicate-12",
            () => PlateLibrary.Duplicate(
                plate,
                TestDomainValues.Timestamp,
                duplicateId),
            iterationsPerSample: 4_096);

        Assert.True(result.P95Microseconds <= 100, result.ToString());
        Assert.True(result.MeanMicroseconds <= 50, result.ToString());
        Assert.True(result.AllocatedBytesPerOperation <= 4_000, result.ToString());
    }

    /// <summary>Measures sorting the maximum 255 dye entries without native resources.</summary>
    [Fact]
    [Trait("Category", "Performance")]
    public void CompleteDyePaletteStaysWithinBudget()
    {
        var dyes = Enumerable.Range(1, 255)
            .Select(id => new DyeOption((byte)id, $"Dye {id}", (uint)(id * 65793) ^ 0x5599AA))
            .ToArray();
        var result = Measure("dye-palette-255", () => DyePaletteOrder.Sort(dyes), 64);
        Assert.True(result.P95Microseconds <= 1_000, result.ToString());
        Assert.True(result.MeanMicroseconds <= 1_000, result.ToString());
        Assert.True(result.AllocatedBytesPerOperation <= 32_000, result.ToString());
    }

    /// <summary>Bounds recurring full-window placement with the larger icon drawer.</summary>
    [Fact]
    [Trait("Category", "Performance")]
    public void CompanionDrawerPlacementStaysWithinBudget()
    {
        var native = new WindowBounds(40, 50, 800, 1200);
        var viewport = new WindowBounds(0, 0, 3840, 2160);
        var result = Measure("companion-icon-drawer-placement",
            () => CompanionPlacementPolicy.Calculate(native, viewport, 436)!.Value, 4096);
        Assert.True(result.P95Microseconds <= 50, result.ToString());
        Assert.True(result.MeanMicroseconds <= 50, result.ToString());
        // The harness boxes value-type results for KeepAlive; this allowance includes that cost.
        Assert.True(result.AllocatedBytesPerOperation <= 64, result.ToString());
    }

    /// <summary>Bounds rollback snapshots and view rebuilding while switching large libraries.</summary>
    [Theory]
    [InlineData(100, 10_000, 1_000_000)]
    [InlineData(1_000, 50_000, 8_000_000)]
    [Trait("Category", "Performance")]
    public void LibrarySelectionStaysWithinBudget(int count, int maxP95Microseconds, int maxBytes)
    {
        var plates = Enumerable.Range(0, count).Select(index =>
        {
            var plate = TestDomainValues.Plate($"Outfit {index:D4}");
            plate.Id = Guid.NewGuid();
            plate.LibraryOrder = index;
            for (var slot = 0; slot < PlateRules.SlotCount; slot++)
                plate.Items[slot] = new SavedPlateItem { ItemId = (uint)(slot + 1) };
            return plate;
        }).ToArray();
        using var controller = new MainController(new FakePlateLibraryStore(plates),
            new FakeNativeCollectionValidator(), new FakeSharedCollectionSnapshotSource(),
            new FakePlateEditor(), new FakeItemFacts());
        var next = 0;
        var result = Measure($"library-select-{count}", () =>
        {
            next = (next + 1) % count;
            controller.Execute(new MainCommand.SelectPlate(plates[next].Id),
                TestDomainValues.Timestamp, Guid.Empty);
            return controller.View;
        }, 4);
        Assert.True(result.P95Microseconds <= maxP95Microseconds, result.ToString());
        Assert.True(result.MeanMicroseconds <= maxP95Microseconds, result.ToString());
        Assert.True(result.AllocatedBytesPerOperation <= maxBytes, result.ToString());
    }

    private PerformanceResult Measure(
        string scenario,
        Func<object> operation,
        int iterationsPerSample)
    {
        for (var warmup = 0; warmup < 16; warmup++)
            GC.KeepAlive(operation());

        var timings = new double[SampleCount];
        var allocations = new long[SampleCount];
        for (var sample = 0; sample < SampleCount; sample++)
        {
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            for (var iteration = 0; iteration < iterationsPerSample; iteration++)
                GC.KeepAlive(operation());
            var elapsed = Stopwatch.GetElapsedTime(started);
            var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

            timings[sample] = elapsed.TotalMicroseconds / iterationsPerSample;
            allocations[sample] = (allocatedAfter - allocatedBefore) / iterationsPerSample;
        }

        Array.Sort(timings);
        Array.Sort(allocations);
        var result = new PerformanceResult(
            scenario,
            SampleCount,
            timings.Average(),
            timings[(int)Math.Ceiling(SampleCount * 0.95) - 1],
            (long)allocations.Average());
        output.WriteLine(result.ToString());
        return result;
    }

    private sealed record PerformanceResult(
        string Scenario,
        int Samples,
        double MeanMicroseconds,
        double P95Microseconds,
        long AllocatedBytesPerOperation)
    {
        public override string ToString()
            => string.Create(
                CultureInfo.InvariantCulture,
                $"PERF|{Scenario}|samples={Samples}|mean_us={MeanMicroseconds:F3}|p95_us={P95Microseconds:F3}|alloc_bytes={AllocatedBytesPerOperation}");
    }
}

/// <summary>Prevents ordinary parallel tests from contaminating timing samples.</summary>
[CollectionDefinition("Performance measurements", DisableParallelization = true)]
public sealed class PerformanceMeasurementGroup;
