using System.Collections.Immutable;
using SpringCleaning.Shared.CollectionData;

namespace SpringCleaning.Tests.Features.Inventory;

public sealed class DresserRestorationPolicyTests
{
    private static DresserAlertEntry Outfit => new(1, 100, 100, false, true, 1, "", "Outfit", 1,
        DresserAlertKind.ArmoireEligible, true, false, false)
    { Components = [new(0, 42), new(1, 43)] };

    private static CollectionDataSnapshot Snapshot(DresserAlertEntry entry, params uint[] armoireIds)
    {
        var empty = new CollectionSourceSnapshot(CollectionSourceStatus.Fresh, DateTimeOffset.UnixEpoch, [], null);
        return new("Provider", "1", 42, 1, DateTimeOffset.UnixEpoch,
            empty, empty, empty, empty, empty,
            empty with { ItemIds = armoireIds.Order().ToImmutableArray() }, empty)
        {
            DresserEntries = [new(entry.PrismBoxIndex, entry.RawItemId, entry.ItemId,
                entry.IsOutfit ? 100u : null, entry.Components.ToImmutableArray())],
        };
    }

    [Fact]
    public void EveryStoredOutfitComponentMustBeRetained()
    {
        Assert.True(DresserRestorationPolicy.CanPlan(Snapshot(Outfit, 42, 43), Outfit));
        Assert.False(DresserRestorationPolicy.CanPlan(Snapshot(Outfit, 42), Outfit));
        Assert.False(DresserRestorationPolicy.CanPlan(Snapshot(Outfit, 43), Outfit));
    }

    [Fact]
    public void SingleEntryNeedsItsOwnRetainedAppearance()
    {
        var single = Outfit with { IsOutfit = false, ItemId = 42, RawItemId = 42, Components = [] };
        Assert.Equal(new uint[] { 42 }, DresserRestorationPolicy.Components(single));
        Assert.True(DresserRestorationPolicy.CanPlan(Snapshot(single, 42), single));
        Assert.False(DresserRestorationPolicy.CanPlan(Snapshot(single), single));
    }

    [Fact]
    public void UnknownStaleOrMissingCompositionNeverAuthorizesRestoration()
    {
        var snapshot = Snapshot(Outfit, 42, 43);
        Assert.False(DresserRestorationPolicy.CanPlan(null, Outfit));
        Assert.False(DresserRestorationPolicy.CanPlan(snapshot with { DresserEntries = null }, Outfit));
        Assert.False(DresserRestorationPolicy.CanPlan(snapshot with
        { GlamourDresser = snapshot.GlamourDresser with { Status = CollectionSourceStatus.Stale } }, Outfit));
        Assert.False(DresserRestorationPolicy.CanPlan(snapshot with
        { Armoire = snapshot.Armoire with { Status = CollectionSourceStatus.Unavailable, ItemIds = [] } }, Outfit));
    }

    [Fact]
    public void SelectionMustMatchCompleteCurrentEntry()
    {
        var snapshot = Snapshot(Outfit, 42, 43);
        Assert.False(DresserRestorationPolicy.CanPlan(snapshot, Outfit with { PrismBoxIndex = 2 }));
        Assert.False(DresserRestorationPolicy.CanPlan(snapshot, Outfit with { RawItemId = 101 }));
        Assert.False(DresserRestorationPolicy.CanPlan(snapshot, Outfit with { Components = [] }));
        var emptyOutfit = Outfit with { Components = [] };
        Assert.False(DresserRestorationPolicy.CanPlan(Snapshot(emptyOutfit), emptyOutfit));
        Assert.False(DresserRestorationPolicy.CanPlan(snapshot, Outfit with { Components = [new(0, 42)] }));
        Assert.False(DresserRestorationPolicy.CanPlan(snapshot, Outfit with { IsOutfit = false, ItemId = 42 }));
    }

    [Fact]
    public void FindsExactEntryAmongUnrelatedEntriesAndRejectsEmptyDresser()
    {
        var snapshot = Snapshot(Outfit, 42, 43);
        var exact = snapshot.DresserEntries!.Value[0];
        Assert.True(DresserRestorationPolicy.CanPlan(snapshot with
        { DresserEntries = [exact with { EntryIndex = 2 }, exact] }, Outfit));
        Assert.False(DresserRestorationPolicy.CanPlan(snapshot with { DresserEntries = [] }, Outfit));
    }
    [Fact]
    public void EntryKindAndBaseIdentityCannotBeSubstituted()
    {
        var snapshot = Snapshot(Outfit, 42, 43);
        Assert.False(DresserRestorationPolicy.CanPlan(snapshot with
        { DresserEntries = [snapshot.DresserEntries!.Value[0] with { OutfitId = null }] }, Outfit));
        var single = Outfit with { IsOutfit = false, ItemId = 42, RawItemId = 42, Components = [] };
        snapshot = Snapshot(single, 42);
        Assert.False(DresserRestorationPolicy.CanPlan(snapshot with
        { DresserEntries = [snapshot.DresserEntries!.Value[0] with { BaseItemId = 43 }] }, single));
    }
}
