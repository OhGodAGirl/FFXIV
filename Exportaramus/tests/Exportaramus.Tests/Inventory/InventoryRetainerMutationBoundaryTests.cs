using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class InventoryRetainerMutationBoundaryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-13T12:00:00Z");
    private static InventoryLocationSnapshot Location(ulong id) => new(new(InventoryLocationKind.Retainer, id), "Retainer",
        InventoryObservationState.Observed, Now, Now, [new(100, 1, 0)]);
    private static InventorySnapshot Snapshot(IEnumerable<InventoryLocationSnapshot> locations) => new(1, 1, true, [.. locations]) { RetainerRosterObservedAtUtc = Now };
    private static CurrentItemPresenceState Capture(InventorySnapshot snapshot) => InventoryRetainerPresencePolicy.Capture(snapshot, 1, true, 1, Now);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultipleRetainedLocationsRemainStaleWithExplicitProvenance(bool restored)
    {
        var result = Capture(Snapshot(new[] { Location(11), Location(22) }.Select(location => location with
        { State = InventoryObservationState.Retained, RestoredFromCache = restored })));
        Assert.Equal(SourceStatus.Stale, result.Health.SourceStatus);
        Assert.Equal(restored ? ProvenanceType.Restored : ProvenanceType.Observed, result.Health.ProvenanceType);
        Assert.Equal(restored ? ReasonCode.RestoredFromDisk : ReasonCode.WindowNotReady, result.Health.ReasonCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Health.Reason));
        Assert.Equal(2u, Assert.Single(result.Items).Quantity);
    }

    [Fact]
    public void ExactlyMaximumLocationsIsAcceptedAndOneMoreIsRejectedBeforeAggregation()
    {
        var locations = Enumerable.Range(1, 10).Select(id => Location((ulong)id))
            .Concat(Enum.GetValues<InventoryLocationKind>().Where(kind => kind != InventoryLocationKind.Retainer)
                .Select(kind => InventoryPolicy.Unknown(new(kind), kind.ToString()))).ToArray();
        Assert.Equal(17, locations.Length);
        Assert.True(Capture(Snapshot(locations)).Health.IsUsable);
        var tooMany = Capture(Snapshot(locations.Append(InventoryPolicy.Unknown(new(InventoryLocationKind.Inventory), "Extra"))));
        Assert.Equal(ReasonCode.IncompleteRetainerSet, tooMany.Health.ReasonCode);
        Assert.Empty(tooMany.Items);
        Assert.Equal(CapabilityState.Supported, tooMany.Health.CapabilityState);
        Assert.True(tooMany.SupportsQuantity);
    }

    [Fact]
    public void ExactlyMaximumUniqueLocationItemsAreAccepted()
    {
        var items = Enumerable.Range(1, 50000).Select(id => new InventoryItemQuantity((uint)id, 1, 0)).ToImmutableArray();
        var result = Capture(Snapshot([Location(11) with { Items = items }]));
        Assert.True(result.Health.IsUsable);
        Assert.Equal(50000, result.Items.Length);
        Assert.Equal(50000u, result.Items[^1].ItemId);
    }

    [Fact]
    public async Task RosterReadAtCaptureTimeAndEqualTimestampReplacementAreAccepted()
    {
        await using var service = new InventoryService();
        service.SetCharacter(1);
        Assert.True(service.ObserveRoster(1, [], Now));
        Assert.True(Capture(service.GetSnapshot()!).Health.IsUsable);
        Assert.Equal(Now, Capture(service.GetSnapshot()!).Health.ObservedAtUtc);
        Assert.True(service.ObserveRoster(1, [new(11, "Retainer")], Now));
        Assert.Equal(11ul, Assert.Single(service.GetSnapshot()!.Locations, location => location.Key.Kind == InventoryLocationKind.Retainer).Key.RetainerId);
    }

    [Fact]
    public void RetainerOrderAloneDoesNotRequestCapture()
    {
        var observer = new RetainerInventoryChangeObserver();
        Assert.True(observer.Observe(Snapshot([Location(11), Location(22)])));
        Assert.False(observer.Observe(Snapshot([Location(22), Location(11)]) with { Revision = 2 }));
        Assert.False(observer.Observe(Snapshot([Location(11), Location(22)]) with { Revision = 3 }));
    }
}

