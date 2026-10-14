using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Exportaramus.Features.CollectionData.Capture.Retainers;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Tests.Snapshots;

namespace Exportaramus.Tests.Capture;

public sealed class RetainerCacheLifetimeTests
{
    [Fact]
    public void ResetReleasesTheDepartingCharactersCachedItemArray()
    {
        var accumulator = new RetainerAccumulator();
        var payload = Populate(accumulator);
        accumulator.Reset();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(payload.TryGetTarget(out _));
        GC.KeepAlive(accumulator);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<PresenceItem[]> Populate(RetainerAccumulator accumulator)
    {
        var items = ImmutableArray.Create(new PresenceItem(100, 1));
        var state = new CurrentItemPresenceState(items, true, SnapshotFixture.Health(DataType.CurrentItemPresence));
        Assert.True(accumulator.Observe(123, [99], 99, state, 1, SnapshotFixture.Now).Health.IsUsable);
        return new(ImmutableCollectionsMarshal.AsArray(items)!);
    }
}

