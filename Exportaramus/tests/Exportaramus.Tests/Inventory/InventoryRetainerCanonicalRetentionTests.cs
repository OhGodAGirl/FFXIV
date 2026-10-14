using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class InventoryRetainerCanonicalRetentionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-13T12:00:00Z");
    private static CurrentItemPresenceState Capture(InventorySnapshot snapshot) =>
        InventoryRetainerPresencePolicy.Capture(snapshot, 1, true, 5, Now.AddHours(1));

    [Fact]
    public async Task IncompleteReplacementRosterRetainsStaleThenCompleteReadRemovesOldQuantities()
    {
        await using var service = new InventoryService();
        service.SetCharacter(1);
        service.ObserveRoster(1, [new(11, "Old")], Now);
        service.AcceptLocation(1, new(InventoryLocationKind.Retainer, 11), "Old", [new(100, 4, 2)], Now);
        var prior = Capture(service.GetSnapshot()!);
        service.ObserveRoster(1, [new(22, "New")], Now.AddMinutes(1));
        var incomplete = Capture(service.GetSnapshot()!);
        var retained = CollectionStatePolicy.Resolve(prior, incomplete, true, true);
        Assert.Equal(SourceStatus.Stale, retained.Health.SourceStatus);
        Assert.Equal(ReasonCode.IncompleteRetainerSet, retained.Health.ReasonCode);
        Assert.Equal(Now, retained.Health.ObservedAtUtc);
        Assert.Equal(100u, Assert.Single(retained.Items).ItemId);
        service.AcceptLocation(1, new(InventoryLocationKind.Retainer, 22), "New", [new(200, 1, 0)], Now.AddMinutes(2));
        var replacement = CollectionStatePolicy.Resolve(retained, Capture(service.GetSnapshot()!), true, true);
        Assert.Equal(SourceStatus.Fresh, replacement.Health.SourceStatus);
        Assert.Equal(Now.AddMinutes(2), replacement.Health.ObservedAtUtc);
        Assert.Equal(200u, Assert.Single(replacement.Items).ItemId);
    }

    [Fact]
    public async Task RestoreThenFreshReadReplacesCachedItemsAndProvenance()
    {
        var key = new InventoryLocationKey(InventoryLocationKind.Retainer, 11);
        var saved = new InventorySnapshot(1, 1, true,
            [new(key, "Retainer", InventoryObservationState.Observed, Now.AddDays(-1), Now.AddDays(-1), [new(100, 8, 0)])]);
        await using var service = new InventoryService(new Cache(saved));
        service.SetCharacter(1);
        service.ObserveRoster(1, [new(11, "Retainer")], Now);
        service.Pump();
        for (var attempt = 0; attempt < 100 && !service.GetSnapshot()!.Locations.Single(location => location.Key == key).HasObservation; attempt++)
        {
            await Task.Delay(10);
            service.Pump();
        }
        var restored = Capture(service.GetSnapshot()!);
        Assert.Equal(SourceStatus.Stale, restored.Health.SourceStatus);
        Assert.Equal(ProvenanceType.Restored, restored.Health.ProvenanceType);
        Assert.Equal(Now.AddDays(-1), restored.Health.ObservedAtUtc);
        Assert.False(service.MarkUnavailable(1, key, "Retainer", Now.AddMinutes(1), false));
        Assert.True(service.GetSnapshot()!.Locations.Single(location => location.Key == key).RestoredFromCache);
        Assert.True(service.AcceptLocation(1, key, "Retainer", [new(200, 1, 2)], Now.AddMinutes(2)));
        var fresh = CollectionStatePolicy.Resolve(restored, Capture(service.GetSnapshot()!), true, true);
        Assert.Equal(SourceStatus.Fresh, fresh.Health.SourceStatus);
        Assert.Equal(ProvenanceType.Observed, fresh.Health.ProvenanceType);
        Assert.Equal(ReasonCode.None, fresh.Health.ReasonCode);
        Assert.Equal(Now.AddMinutes(2), fresh.Health.ObservedAtUtc);
        Assert.Equal(200u, Assert.Single(fresh.Items).ItemId);
        Assert.False(service.GetSnapshot()!.Locations.Single(location => location.Key == key).RestoredFromCache);
    }

    private sealed class Cache(InventorySnapshot saved) : IInventoryCache
    {
        public Task<InventorySnapshot?> LoadAsync(ulong contentId, CancellationToken cancellationToken) => Task.FromResult<InventorySnapshot?>(saved);
        public Task SaveAsync(InventorySnapshot snapshot, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

