namespace Exportaramus.Features.Inventory;

/// <summary>Tracks immutable revisions while ignoring changes confined to non-Retainer locations.</summary>
public sealed class RetainerInventoryChangeObserver
{
    private InventorySnapshot? previous;

    public bool Observe(InventorySnapshot? current)
    {
        var prior = previous;
        previous = current;
        if (current is null) return prior is not null;
        if (prior is null || prior.ContentId != current.ContentId) return true;
        if (prior.Revision == current.Revision) return false;
        if (prior.RetainerRosterKnown != current.RetainerRosterKnown || prior.RetainerRosterObservedAtUtc != current.RetainerRosterObservedAtUtc) return true;
        var before = prior.Locations.Where(location => location.Key.Kind == InventoryLocationKind.Retainer)
            .OrderBy(location => location.Key.RetainerId).ToArray();
        var after = current.Locations.Where(location => location.Key.Kind == InventoryLocationKind.Retainer)
            .OrderBy(location => location.Key.RetainerId).ToArray();
        return before.Length != after.Length || before.Where((location, index) =>
            location.RestoredFromCache != after[index].RestoredFromCache || location.Key != after[index].Key || location.State != after[index].State
            || location.ObservedAtUtc != after[index].ObservedAtUtc
            || !location.Items.SequenceEqual(after[index].Items)).Any();
    }
}

