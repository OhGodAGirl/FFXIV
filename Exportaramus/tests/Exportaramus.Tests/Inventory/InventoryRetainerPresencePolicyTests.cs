using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class InventoryRetainerPresencePolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-13T12:00:00Z");
    private static InventoryLocationSnapshot Location(ulong id = 11) => new(new(InventoryLocationKind.Retainer, id),
        "Retainer", InventoryObservationState.Observed, Now.AddHours(-2), Now, [new(100, 2, 3)]);
    private static InventorySnapshot Snapshot(params InventoryLocationSnapshot[] locations) => new(1, 1, true, [.. locations]) { RetainerRosterObservedAtUtc = Now.AddHours(-3) };
    private static CurrentItemPresenceState Read(InventorySnapshot? snapshot, ulong id = 1, bool trusted = true,
        DateTimeOffset? at = null) => InventoryRetainerPresencePolicy.Capture(snapshot, id, trusted, 9, at ?? Now);

    [Fact]
    public void CompleteObservationsSumBothQualitiesAndPreserveOldestRead()
    {
        var result = Read(Snapshot(Location(), Location(22) with { ObservedAtUtc = Now.AddHours(-1), Items = [new(100, 7, 11), new(200, 0, 2)] }));
        Assert.Equal(new PresenceItem[] { new(100, 23), new(200, 2) }, result.Items.ToArray());
        Assert.True(result.SupportsQuantity);
        Assert.Equal(SourceStatus.Fresh, result.Health.SourceStatus);
        Assert.Equal(ProvenanceType.Observed, result.Health.ProvenanceType);
        Assert.Equal(Now.AddHours(-2), result.Health.ObservedAtUtc);
        Assert.Equal(9, result.Health.CaptureGeneration);
    }

    [Fact]
    public void RetainedObservationNeverBecomesFresh()
    {
        var result = Read(Snapshot(Location() with { State = InventoryObservationState.Retained }, Location(22)));
        Assert.Equal(SourceStatus.Stale, result.Health.SourceStatus);
        Assert.Equal(ProvenanceType.Observed, result.Health.ProvenanceType);
        Assert.Equal(ReasonCode.WindowNotReady, result.Health.ReasonCode);
        Assert.Equal(Now.AddHours(-2), result.Health.ObservedAtUtc);
        Assert.Equal(10u, Assert.Single(result.Items).Quantity);
    }

    [Fact]
    public void KnownCompleteEmptyIsUsableButUnknownRosterIsNot()
    {
        Assert.True(Read(Snapshot()).Health.IsUsable);
        Assert.Empty(Read(Snapshot(Location() with { Items = [] })).Items);
        Assert.True(Read(Snapshot(Location() with { Items = [] })).Health.IsUsable);
        Assert.False(Read(Snapshot() with { RetainerRosterKnown = false }).Health.IsUsable);
    }

    [Fact]
    public void IdentityAndBuildFailuresExposeNoItems()
    {
        foreach (var result in new[] { Read(null), Read(Snapshot(Location()), 0), Read(Snapshot(Location()), 2), Read(Snapshot(Location()), trusted: false) })
        {
            Assert.False(result.Health.IsUsable);
            Assert.Empty(result.Items);
        }
        Assert.Equal(CapabilityState.UnavailablePendingValidation, Read(Snapshot(Location()), trusted: false).Health.CapabilityState);
    }

    [Fact]
    public void InvalidOrIncompleteObservationsFailClosed()
    {
        var bad = new[] {
            Location() with { State = InventoryObservationState.Unknown },
            Location() with { State = InventoryObservationState.ReadFailed },
            Location() with { State = (InventoryObservationState)99 },
            Location(0), Location() with { ObservedAtUtc = null },
            Location() with { ObservedAtUtc = default(DateTimeOffset) },
            Location() with { ObservedAtUtc = Now.AddTicks(1) },
            Location() with { Items = default }, Location() with { Items = [null!] },
            Location() with { Items = [new(0, 1, 0)] }, Location() with { Items = [new(1000000, 1, 0)] },
            Location() with { Items = [new(1, 0, 0)] }, Location() with { Items = [new(1, 1, 0), new(1, 2, 0)] },
            Location() with { Items = [new(1, uint.MaxValue, 1)] },
            Location() with { Items = Enumerable.Repeat(new InventoryItemQuantity(1, 1, 0), 50001).ToImmutableArray() },
        };
        foreach (var location in bad)
        {
            var result = Read(Snapshot(location));
            Assert.False(result.Health.IsUsable);
            Assert.Empty(result.Items);
        }
        Assert.False(Read(Snapshot((InventoryLocationSnapshot)null!)).Health.IsUsable);
        Assert.False(Read(Snapshot() with { Locations = default }).Health.IsUsable);
        Assert.False(Read(Snapshot(Enumerable.Repeat(Location(), 18).ToArray())).Health.IsUsable);
        Assert.False(Read(Snapshot(), at: default(DateTimeOffset)).Health.IsUsable);
        Assert.False(Read(Snapshot(Location(), Location())).Health.IsUsable);
        Assert.True(Read(Snapshot(Enumerable.Range(1, 10).Select(i => Location((ulong)i)).ToArray())).Health.IsUsable);
        Assert.False(Read(Snapshot(Enumerable.Range(1, 11).Select(i => Location((ulong)i)).ToArray())).Health.IsUsable);
    }

    [Fact]
    public void AggregateOverflowFailsAndNonRetainerItemsDoNotContribute()
    {
        var maximum = Location() with { Items = [new(1, uint.MaxValue, 0)] };
        Assert.Equal(uint.MaxValue, Assert.Single(Read(Snapshot(maximum)).Items).Quantity);
        Assert.Equal(ReasonCode.QuantityOverflow, Read(Snapshot(maximum with { State = InventoryObservationState.Retained }, Location(22) with { Items = [new(1, 1, 0)] })).Health.ReasonCode);
        Assert.Equal(ReasonCode.QuantityOverflow, Read(Snapshot(maximum, Location(22) with { Items = [new(1, 1, 0)] })).Health.ReasonCode);
        Assert.Single(Read(Snapshot(Location(), Location() with { Key = new(InventoryLocationKind.Inventory) })).Items);
    }
}





