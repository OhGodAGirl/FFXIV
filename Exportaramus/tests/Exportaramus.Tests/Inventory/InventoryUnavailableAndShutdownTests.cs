using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class InventoryUnavailableAndShutdownTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task UnavailableBeforeAnyObservationRemainsUnknownOnTheWire()
    {
        var service = new InventoryService();
        try
        {
            service.SetCharacter(1);
            var before = service.GetSnapshot()!;
            var key = new InventoryLocationKey(InventoryLocationKind.Inventory);
            var location = before.Locations.Single(value => value.Key == key);
            Assert.Equal(InventoryObservationState.Unknown, location.State);
            Assert.False(location.HasObservation);

            Assert.False(service.MarkUnavailable(1, key, location.Name,
                new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero), false));

            Assert.Same(before, service.GetSnapshot());
            using var json = JsonDocument.Parse(InventoryQuery.Snapshot(service.GetSnapshot()));
            var wire = json.RootElement.GetProperty("locations").EnumerateArray()
                .Single(value => value.GetProperty("kind").GetString() == "Inventory");
            Assert.Equal("Unknown", wire.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, wire.GetProperty("observedAtUtc").ValueKind);
            Assert.Empty(wire.GetProperty("items").EnumerateArray());
        }
        finally
        {
            await service.DisposeAsync().AsTask().WaitAsync(Deadline);
        }
    }

    [Fact]
    public async Task LogoutDoesNotStartACacheReadForTheAbsentCharacter()
    {
        var cache = new RecordingCache();
        var service = new InventoryService(cache);
        try
        {
            service.SetCharacter(1);
            service.Pump();
            await SettleOwnedWorker(service);
            service.Pump();
            Assert.Equal([1UL], cache.Loads.ToArray());

            service.SetCharacter(0);
            service.Pump();
            await SettleOwnedWorker(service);
            service.Pump();

            Assert.Equal([1UL], cache.Loads.ToArray());
            Assert.Null(service.GetSnapshot());
            Assert.Null(service.LastCacheFailure);
        }
        finally
        {
            await service.DisposeAsync().AsTask().WaitAsync(Deadline);
        }
    }

    [Fact]
    public async Task ShutdownReleasesQueuedCharacterPayloadsAfterSavingThem()
    {
        var cache = new RecordingCache();
        var service = new InventoryService(cache);
        try
        {
            service.SetCharacter(1);
            Assert.True(service.ObserveRoster(1, [new(11, "First retainer")]));
            service.SetCharacter(2);
            Assert.True(service.ObserveRoster(2, [new(22, "Second retainer")]));
            var field = typeof(InventoryService).GetField("pendingWrites", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var queue = Assert.IsType<Dictionary<ulong, InventorySnapshot>>(field.GetValue(service));
            Assert.Equal([1UL, 2UL], queue.Keys.Order().ToArray());

            await service.DisposeAsync().AsTask().WaitAsync(Deadline);

            Assert.Equal([1UL, 2UL], cache.Saves.Order().ToArray());
            Assert.Empty(queue);
            Assert.Null(service.GetSnapshot());
            await service.DisposeAsync().AsTask().WaitAsync(Deadline);
            Assert.Equal(2, cache.Saves.Count);
        }
        finally
        {
            await service.DisposeAsync().AsTask().WaitAsync(Deadline);
        }
    }

    private static async Task SettleOwnedWorker(InventoryService service)
    {
        // The owned-task barrier makes the no-new-load assertion deterministic without sleeping.
        var field = typeof(InventoryService).GetField("worker", BindingFlags.Instance | BindingFlags.NonPublic)!;
        if (field.GetValue(service) is Task worker)
            await worker.WaitAsync(Deadline);
    }

    private sealed class RecordingCache : IInventoryCache
    {
        public ConcurrentQueue<ulong> Loads { get; } = new();
        public ConcurrentQueue<ulong> Saves { get; } = new();

        public Task<InventorySnapshot?> LoadAsync(ulong contentId, CancellationToken cancellationToken)
        {
            Loads.Enqueue(contentId);
            return Task.FromResult<InventorySnapshot?>(null);
        }

        public Task SaveAsync(InventorySnapshot snapshot, CancellationToken cancellationToken)
        {
            Saves.Enqueue(snapshot.ContentId);
            return Task.CompletedTask;
        }
    }
}
