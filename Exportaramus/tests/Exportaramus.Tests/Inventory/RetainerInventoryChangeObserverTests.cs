using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class RetainerInventoryChangeObserverTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-13T12:00:00Z");
    private static readonly InventoryLocationSnapshot Retainer = new(new(InventoryLocationKind.Retainer, 11), "Retainer",
        InventoryObservationState.Observed, Now, Now, [new(100, 1, 0)]);
    private static InventorySnapshot Snapshot(long revision, params InventoryLocationSnapshot[] locations) => new(1, revision, true, [.. locations]);

    [Fact]
    public void IdentityBoundariesClearPriorStateAndRevisionAvoidsRepeatedWork()
    {
        var observer = new RetainerInventoryChangeObserver();
        Assert.False(observer.Observe(null));
        var initial = Snapshot(1, Retainer);
        Assert.True(observer.Observe(initial));
        Assert.False(observer.Observe(initial));
        Assert.True(observer.Observe(initial with { ContentId = 2 }));
        Assert.True(observer.Observe(null));
        Assert.False(observer.Observe(null));
        Assert.True(observer.Observe(initial));
    }

    [Fact]
    public void BagChangesAndRetainerNameOrAttemptChangesDoNotRequestPresence()
    {
        var observer = new RetainerInventoryChangeObserver();
        observer.Observe(Snapshot(1, Retainer));
        Assert.False(observer.Observe(Snapshot(2, Retainer with { Name = "Renamed", AttemptedAtUtc = Now.AddMinutes(1) },
            Retainer with { Key = new(InventoryLocationKind.Inventory), Items = [new(200, 50, 0)] })));
        Assert.False(observer.Observe(Snapshot(3, Retainer)));
    }

    [Fact]
    public void RosterContentsStateAndReadTimeChangesRequestPresence()
    {
        var variants = new[] {
            Snapshot(2), Snapshot(2, Retainer) with { RetainerRosterKnown = false },
            Snapshot(2, Retainer with { Key = new(InventoryLocationKind.Retainer, 22) }),
            Snapshot(2, Retainer with { State = InventoryObservationState.Retained }),
            Snapshot(2, Retainer with { ObservedAtUtc = Now.AddMinutes(1) }),
            Snapshot(2, Retainer with { ObservedAtUtc = null }),
            Snapshot(2, Retainer with { Items = [new(100, 2, 0)] }),
            Snapshot(2, Retainer with { Items = [] }),
        };
        foreach (var changed in variants)
        {
            var observer = new RetainerInventoryChangeObserver();
            observer.Observe(Snapshot(1, Retainer));
            Assert.True(observer.Observe(changed));
            Assert.False(observer.Observe(changed with { Revision = 3 }));
        }
    }
}

