using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class InventoryMutationRegressionTests : IDisposable
{
    private readonly string directory = Path.Combine(AppContext.BaseDirectory, "inventory-mutation", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-13T00:00:00Z");
    private static readonly InventoryLocationKey Bag = new(InventoryLocationKind.Inventory);

    [Fact]
    public async Task CachePathAndMaximumLengthPreserveTheContract()
    {
        var cache = new InventoryFileCache(directory);
        Assert.Equal(Path.Combine(directory, "snapshots", "characters", "1", "inventory.json"), cache.CharacterPath(1));
        await cache.SaveAsync(InventoryPolicy.Empty(1, 0), default);
        var bytes = await File.ReadAllBytesAsync(cache.CharacterPath(1));
        var padded = Enumerable.Repeat((byte)' ', InventoryFileCache.MaximumBytes).ToArray();
        bytes.CopyTo(padded, 0);
        await File.WriteAllBytesAsync(cache.CharacterPath(1), padded);
        using var held = new FileStream(cache.CharacterPath(1), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Assert.Equal(0, (await cache.LoadAsync(1, default))!.Revision);
    }

    [Fact]
    public async Task TenRetainersAndSevenStandardLocationsRoundTrip()
    {
        var initial = InventoryPolicy.Empty(1, 0);
        var retainers = Enumerable.Range(1, 10).Reverse().Select(i => InventoryPolicy.Unknown(new(InventoryLocationKind.Retainer, (ulong)i), "Retainer " + i));
        var locations = InventoryPolicy.Order(initial.Locations.Concat(retainers));
        Assert.Equal(Enumerable.Range(1, 10).Select(i => (ulong)i), locations.Where(x => x.Key.Kind == InventoryLocationKind.Retainer).Select(x => x.Key.RetainerId));
        var cache = new InventoryFileCache(directory);
        await cache.SaveAsync(initial with { Locations = locations }, default);
        Assert.Equal(17, (await cache.LoadAsync(1, default))!.Locations.Length);
    }

    [Fact]
    public void ExactItemLimitIsAccepted()
    {
        Assert.True(InventoryPolicy.TryNormalize(Enumerable.Range(1, 50000).Select(i => new InventoryItemQuantity((uint)i, 1, 0)), out var items));
        Assert.Equal(50000, items.Length);
    }

    [Fact]
    public async Task EqualTimestampsCanChangeItemsAndAvailabilityWithoutLosingRosterData()
    {
        await using var service = new InventoryService();
        service.SetCharacter(1);
        var firstRevision = service.GetSnapshot()!.Revision;
        Assert.True(firstRevision > 0);
        var key = new InventoryLocationKey(InventoryLocationKind.Retainer, 11);
        service.ObserveRoster(1, [new(11, "First")]);
        Assert.True(service.AcceptLocation(1, key, "First", [new(100, 1, 0)], Now));
        Assert.True(service.AcceptLocation(1, key, "First", [new(101, 1, 0)], Now));
        Assert.True(service.ObserveRoster(1, [new(11, "Renamed")]));
        var row = service.GetSnapshot()!.Locations.Single(x => x.Key == key);
        Assert.Equal(101u, Assert.Single(row.Items).ItemId);
        Assert.Equal(Now, row.ObservedAtUtc);
        Assert.True(service.MarkUnavailable(1, key, "Renamed", Now, true));
        Assert.True(service.MarkUnavailable(1, key, "Renamed", Now, false));
        Assert.Equal(InventoryObservationState.Retained, service.GetSnapshot()!.Locations.Single(x => x.Key == key).State);
        Assert.True(service.GetSnapshot()!.Revision > firstRevision);
    }

    [Fact]
    public void QueryExposesRevisionRosterAndMembershipOnlyNullQuantities()
    {
        var bag = new InventoryLocationSnapshot(Bag, "Bag", InventoryObservationState.Observed, Now, Now, [new(100, 2, 3)]);
        var dresser = new InventoryLocationSnapshot(new(InventoryLocationKind.GlamourDresser), "Dresser", InventoryObservationState.Retained, Now, Now, [new(100, 0, 0)]);
        var snapshot = new InventorySnapshot(1, 42, true, [bag, dresser]);
        var root = JsonNode.Parse(InventoryQuery.Snapshot(snapshot))!;
        Assert.Equal("1.0", root["schemaVersion"]!.GetValue<string>());
        Assert.Equal(42, root["revision"]!.GetValue<int>());
        Assert.True(root["retainerRosterKnown"]!.GetValue<bool>());
        Assert.False(JsonNode.Parse(InventoryQuery.Snapshot(snapshot with { RetainerRosterKnown = false }))!["retainerRosterKnown"]!.GetValue<bool>());
        Assert.Equal(3, root["locations"]![0]!["items"]![0]!["highQualityQuantity"]!.GetValue<int>());
        Assert.Null(root["locations"]![1]!["items"]![0]!["normalQuantity"]);
        var item = JsonNode.Parse(InventoryQuery.Item(snapshot, 100))!;
        Assert.Equal("1.0", item["schemaVersion"]!.GetValue<string>());
        Assert.Equal(42, item["revision"]!.GetValue<int>());
        Assert.Equal("LastObservedQuantifiedLocationsOnly", item["quantityMeaning"]!.GetValue<string>());
        Assert.Null(item["locations"]![1]!["highQualityQuantity"]);
        Assert.True(item["hasRetainedLocations"]!.GetValue<bool>());
        Assert.False(JsonNode.Parse(InventoryQuery.Item(snapshot with { Locations = [bag] }, 100))!["hasRetainedLocations"]!.GetValue<bool>());
    }

    [Fact]
    public void PresentationDistinguishesUnknownRosterAndCapturedRows()
    {
        Assert.Equal("Unknown", InventoryPresentation.Empty.RetainerCount);
        Assert.Equal("Waiting for character", InventoryPresentation.Empty.Summary);
        var unknown = InventoryPresentation.Prepare(InventoryPolicy.Empty(1, 1), null, Now);
        Assert.Equal("Retainer list not yet observed", unknown.Summary);
        var location = new InventoryLocationSnapshot(new(InventoryLocationKind.Retainer, 11), "First", InventoryObservationState.Observed, Now, Now, [new(100, 1, 0)]);
        var known = InventoryPresentation.Prepare(new(1, 1, true, [location]), null, Now);
        Assert.Equal("Retainers: 1/1 captured", known.Summary);
        Assert.Equal("1/1", known.RetainerCount);
        Assert.Contains("1 item types", Assert.Single(known.Locations));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{}")]
    public async Task CorruptCacheUsesStableFailureCode(string contents)
    {
        var cache = new InventoryFileCache(directory);
        Directory.CreateDirectory(Path.GetDirectoryName(cache.CharacterPath(1))!);
        await File.WriteAllTextAsync(cache.CharacterPath(1), contents);
        Assert.Equal("InventoryCacheInvalid", (await Assert.ThrowsAsync<InvalidDataException>(() => cache.LoadAsync(1, default))).Message);
    }

    [Fact]
    public void GuideMessagesDescribeEveryRequestedStepAndExactRosterLimit()
    {
        var port = new GuidePort();
        var guide = new RetainerGuideController(port);
        Assert.Equal("", guide.Message);
        guide.Start(0, TimeSpan.Zero);
        Assert.Equal("Retainer capture is unavailable right now.", guide.Message);
        guide.Start(1, TimeSpan.Zero);
        Assert.Equal("Opening Summoning Bell", guide.Message);
        guide.Update(TimeSpan.Zero);
        Assert.Equal("Reading Retainers", guide.Message);
        guide.Update(TimeSpan.Zero);
        Assert.Equal("Opening Retainer (1/10)", guide.Message);
        foreach (var expected in new[] { "Opening inventory", "Capturing inventory", "Closing inventory", "Returning to Retainers", "Returning to Retainers" })
        {
            guide.Update(TimeSpan.Zero);
            Assert.Equal(expected + " (1/10)", guide.Message);
        }
        guide.Update(TimeSpan.Zero);
        Assert.Equal("Opening Retainer (2/10)", guide.Message);
        guide.Cancel();
        Assert.Equal("Retainer capture cancelled. Captured inventories are saved.", guide.Message);
        guide.Start(1, TimeSpan.Zero);
        guide.Update(TimeSpan.FromMinutes(1));
        Assert.Equal("Retainer capture stopped at OpenBell. Captured inventories are kept; retry when the requested window is ready.", guide.Message);
        guide.Start(1, TimeSpan.Zero);
        port.Safe = false;
        guide.Update(TimeSpan.Zero);
        Assert.Equal("Retainer capture stopped because the character or conditions changed.", guide.Message);
    }

    [Fact]
    public async Task DisposalCancelsOwnedReadAndWaitsBeforeFinalWrite()
    {
        var cache = new DisposalCache();
        var service = new InventoryService(cache);
        service.SetCharacter(1);
        service.AcceptLocation(1, Bag, "Bag", [new(100, 1, 0)], Now);
        service.Pump();
        await cache.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var disposal = service.DisposeAsync().AsTask();
        try
        {
            await cache.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(disposal.IsCompleted);
            Assert.Null(service.GetSnapshot());
            Assert.False(cache.WriteStarted.Task.IsCompleted);
        }
        finally { cache.Release.TrySetResult(); }
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(cache.WriteStarted.Task.IsCompleted);
        Assert.Equal(100u, Assert.Single(cache.Written!.Locations.Single(x => x.Key == Bag).Items).ItemId);
    }

    private sealed class DisposalCache : IInventoryCache
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public InventorySnapshot? Written;
        public async Task<InventorySnapshot?> LoadAsync(ulong contentId, CancellationToken token)
        {
            using var registration = token.Register(() => Cancelled.TrySetResult());
            Started.TrySetResult();
            await Release.Task;
            return null;
        }
        public Task SaveAsync(InventorySnapshot value, CancellationToken token)
        {
            Written = value;
            WriteStarted.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class GuidePort : IRetainerGuidePort
    {
        public bool Safe = true;
        public bool ListReady => true;
        public bool InventoryVisible => false;
        public bool IsSafe(ulong id) => Safe;
        public bool OpenBell() => true;
        public bool TryReadRoster(out ImmutableArray<InventoryRetainer> roster)
        { roster = Enumerable.Range(1, 10).Select(i => new InventoryRetainer((ulong)i, "Retainer")).ToImmutableArray(); return true; }
        public bool SelectRetainer(ulong id) => true;
        public bool OpenInventory(ulong id) => true;
        public bool CaptureInventory(ulong character, ulong id) => true;
        public bool CloseInventory(ulong id) => true;
        public bool QuitRetainer(ulong id) => true;
        public bool CloseOverviewAndInventory() => true;
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
