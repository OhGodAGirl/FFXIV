using System.Text.Json;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class InventoryRetainerRuntimeMetadataTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-13T12:00:00Z");
    private static readonly InventoryLocationKey Key = new(InventoryLocationKind.Retainer, 11);

    [Fact]
    public async Task RosterTimeIsActualAndUnchangedPollingDoesNotRefreshIt()
    {
        await using var service = new InventoryService();
        service.SetCharacter(1);
        Assert.False(service.ObserveRoster(1, [], default(DateTimeOffset)));
        Assert.True(service.ObserveRoster(1, [], Now));
        Assert.False(service.ObserveRoster(1, [], Now.AddMinutes(1)));
        Assert.Equal(Now, service.GetSnapshot()!.RetainerRosterObservedAtUtc);
        Assert.False(service.ObserveRoster(1, [new(11, "Retainer")], Now.AddTicks(-1)));
        var result = InventoryRetainerPresencePolicy.Capture(service.GetSnapshot(), 1, true, 1, Now.AddMinutes(2));
        Assert.Equal(Now, result.Health.ObservedAtUtc);
        Assert.True(result.Health.IsUsable);
    }

    [Fact]
    public async Task RestorePreservesCurrentRosterAndFreshReadClearsCacheProvenance()
    {
        var savedLocation = new InventoryLocationSnapshot(Key, "Retainer", InventoryObservationState.Observed, Now, Now, [new(100, 1, 0)]);
        var saved = new InventorySnapshot(1, 1, true, [savedLocation]) { RetainerRosterObservedAtUtc = Now };
        var restored = InventoryPolicy.Restore(InventoryPolicy.Empty(1, 2), saved);
        Assert.False(restored.RetainerRosterKnown);
        Assert.Null(restored.RetainerRosterObservedAtUtc);
        var location = Assert.Single(restored.Locations, item => item.Key == Key);
        Assert.True(location.RestoredFromCache);
        var known = restored with { RetainerRosterKnown = true };
        var result = InventoryRetainerPresencePolicy.Capture(known, 1, true, 3, Now.AddMinutes(1));
        Assert.Equal(SourceStatus.Stale, result.Health.SourceStatus);
        Assert.Equal(ProvenanceType.Restored, result.Health.ProvenanceType);
        Assert.Equal(ReasonCode.RestoredFromDisk, result.Health.ReasonCode);
        Assert.DoesNotContain("RestoredFromCache", JsonSerializer.Serialize(location));
        Assert.DoesNotContain("RetainerRosterObservedAtUtc", JsonSerializer.Serialize(saved));
        await using var service = new InventoryService();
        service.SetCharacter(1);
        service.ObserveRoster(1, [new(11, "Retainer")], Now);
        service.AcceptLocation(1, Key, "Retainer", [new(100, 1, 0)], Now);
        Assert.False(Assert.Single(service.GetSnapshot()!.Locations, item => item.Key == Key).RestoredFromCache);
    }

    [Fact]
    public void MissingOrFutureEmptyRosterTimestampCannotBecomeFreshEmpty()
    {
        foreach (DateTimeOffset? time in new DateTimeOffset?[] { null, default(DateTimeOffset), Now.AddTicks(1) })
        {
            var snapshot = new InventorySnapshot(1, 1, true, []) { RetainerRosterObservedAtUtc = time };
            Assert.False(InventoryRetainerPresencePolicy.Capture(snapshot, 1, true, 1, Now).Health.IsUsable);
        }
    }

    [Fact]
    public void ObserverDetectsRuntimeProvenanceAndRosterTimestampChanges()
    {
        var location = new InventoryLocationSnapshot(Key, "Retainer", InventoryObservationState.Retained, Now, Now, []);
        var snapshot = new InventorySnapshot(1, 1, true, [location]);
        var observer = new RetainerInventoryChangeObserver();
        observer.Observe(snapshot);
        Assert.True(observer.Observe(snapshot with { Revision = 2, Locations = [location with { RestoredFromCache = true }] }));
        Assert.True(observer.Observe(snapshot with { Revision = 3, RetainerRosterObservedAtUtc = Now }));
        Assert.False(observer.Observe(snapshot with { Revision = 4, RetainerRosterObservedAtUtc = Now }));
        var restoredObserved = location with { State = InventoryObservationState.Observed, RestoredFromCache = true };
        Assert.Equal(SourceStatus.Stale, InventoryRetainerPresencePolicy.Capture(snapshot with { Locations = [restoredObserved] }, 1, true, 1, Now).Health.SourceStatus);
    }
}

