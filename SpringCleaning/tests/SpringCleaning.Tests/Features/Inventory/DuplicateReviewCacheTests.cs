namespace SpringCleaning.Tests;

public sealed class DuplicateReviewCacheTests
{
    [Fact]
    public void UnchangedSnapshotsAndEquivalentIgnoredSetsReuseBothPlans()
    {
        var cache = new DuplicateReviewCache();
        InventoryDuplicateItem[] inventory = [Copy(1), Copy(2)];
        DresserAlertEntry[] dresser = [Entry(3), Entry(4)];
        var first = cache.Get(inventory, dresser, [2, 4]);

        var repeated = cache.Get(inventory, dresser, [4, 2]);

        Assert.Same(first.All, repeated.All);
        Assert.Same(first.Visible, repeated.Visible);
        Assert.Equal([1u, 2u], repeated.All.InventoryDuplicates.Select(copy => copy.ItemId));
        Assert.Equal([3u, 4u], repeated.All.DresserEntries.Select(entry => entry.ItemId));
        Assert.Equal([1u], repeated.Visible.InventoryDuplicates.Select(copy => copy.ItemId));
        Assert.Equal([3u], repeated.Visible.DresserEntries.Select(entry => entry.ItemId));
    }

    [Fact]
    public void ReplacingEitherSnapshotRefreshesTheCorrespondingRows()
    {
        var cache = new DuplicateReviewCache();
        InventoryDuplicateItem[] inventory = [Copy(1)];
        DresserAlertEntry[] dresser = [Entry(2)];
        var first = cache.Get(inventory, dresser, []);

        inventory = [Copy(3)];
        var afterInventory = cache.Get(inventory, dresser, []);
        dresser = [Entry(4)];
        var afterDresser = cache.Get(inventory, dresser, []);

        Assert.NotSame(first.All, afterInventory.All);
        Assert.Equal([3u], afterInventory.Visible.InventoryDuplicates.Select(copy => copy.ItemId));
        Assert.NotSame(afterInventory.All, afterDresser.All);
        Assert.Equal([4u], afterDresser.Visible.DresserEntries.Select(entry => entry.ItemId));
        Assert.Equal([3u], afterDresser.Visible.InventoryDuplicates.Select(copy => copy.ItemId));
    }

    [Fact]
    public void InPlaceIgnoreChangesIncludingEqualSizeReplacementRefreshVisibleRows()
    {
        var cache = new DuplicateReviewCache();
        InventoryDuplicateItem[] inventory = [Copy(1), Copy(2)];
        DresserAlertEntry[] dresser = [];
        HashSet<uint> ignored = [];
        cache.Get(inventory, dresser, ignored);

        ignored.Add(1);
        var afterAdd = cache.Get(inventory, dresser, ignored);
        ignored.Remove(1);
        ignored.Add(2);
        var afterSwap = cache.Get(inventory, dresser, ignored);
        ignored.Clear();
        var afterClear = cache.Get(inventory, dresser, ignored);

        Assert.Equal([2u], afterAdd.Visible.InventoryDuplicates.Select(copy => copy.ItemId));
        Assert.Equal([1u], afterSwap.Visible.InventoryDuplicates.Select(copy => copy.ItemId));
        Assert.Equal(2, afterClear.Visible.Count);
    }

    [Fact]
    public void IgnoringAnyOutfitComponentHidesTheStoredOutfitOnly()
    {
        var cache = new DuplicateReviewCache();
        var outfit = Entry(10) with
        {
            IsOutfit = true,
            Components = [new(0, 20), new(1, 21)],
        };
        var ordinary = Entry(11) with { Components = outfit.Components };

        var result = cache.Get([], [outfit, ordinary], [21]);

        Assert.Equal([ordinary], result.Visible.DresserEntries);
        Assert.True(DuplicateReviewCache.IsIgnored(outfit, [10]));
        Assert.False(DuplicateReviewCache.IsIgnored(outfit, [99]));
        Assert.False(DuplicateReviewCache.IsIgnored(Entry(12) with { IsOutfit = true }, [21]));
    }

    [Fact]
    public void ClearReleasesPresentationAndForcesRebuildForTheSameInputs()
    {
        var cache = new DuplicateReviewCache();
        InventoryDuplicateItem[] inventory = [Copy(1)];
        DresserAlertEntry[] dresser = [Entry(2)];
        var first = cache.Get(inventory, dresser, [1]);

        cache.Clear();
        cache.Clear();
        var rebuilt = cache.Get(inventory, dresser, [1]);
        var withdrawn = cache.Get([], [], []);

        Assert.NotSame(first.All, rebuilt.All);
        Assert.NotSame(first.Visible, rebuilt.Visible);
        Assert.Equal(first.Visible.InventoryDuplicates, rebuilt.Visible.InventoryDuplicates);
        Assert.Equal(first.Visible.DresserEntries, rebuilt.Visible.DresserEntries);
        Assert.Equal(0, withdrawn.All.Count);
        Assert.Equal(0, withdrawn.Visible.Count);
    }

    [Fact]
    public void SeededRefreshesMatchUncachedFilteringWithoutAccumulatingRows()
    {
        var random = new Random(71359);
        var cache = new DuplicateReviewCache();
        HashSet<uint> ignored = [];
        for (var refresh = 0; refresh < 200; refresh++)
        {
            var inventory = Enumerable.Range(1, random.Next(1025)).Select(id => Copy((uint)id)).ToArray();
            var dresser = Enumerable.Range(1, random.Next(801)).Select(id => Entry((uint)id) with
            {
                Kind = id % 3 == 0 ? DresserAlertKind.NotInOutfit : DresserAlertKind.Duplicate,
                IsOutfit = id % 2 == 0,
                Components = [new(0, (uint)(id + 1))],
            }).ToArray();
            ignored.Clear();
            for (var id = 0; id < 15; id++)
                ignored.Add((uint)random.Next(1, 1025));
            var expected = ReviewPlanner.Plan(inventory, dresser, false);

            var actual = cache.Get(inventory, dresser, ignored);

            Assert.Equal(expected.InventoryDuplicates, actual.All.InventoryDuplicates);
            Assert.Equal(expected.DresserEntries, actual.All.DresserEntries);
            Assert.Equal(expected.InventoryDuplicates.Where(copy => !ignored.Contains(copy.ItemId)),
                actual.Visible.InventoryDuplicates);
            Assert.Equal(expected.DresserEntries.Where(entry => !ignored.Contains(entry.ItemId)
                && !DresserRestorationPolicy.Components(entry).Any(ignored.Contains)), actual.Visible.DresserEntries);
            Assert.InRange(actual.All.InventoryDuplicates.Count, 0, 1024);
            Assert.InRange(actual.All.DresserEntries.Count, 0, 800);
        }
    }

    [Fact]
    public void MaximumSizeSnapshotsAreReplacedWithoutAppendingOldRows()
    {
        var cache = new DuplicateReviewCache();
        var inventory = Enumerable.Range(1, 1024).Select(id => Copy((uint)id)).ToArray();
        var dresser = Enumerable.Range(1, 800).Select(id => Entry((uint)id)).ToArray();

        var full = cache.Get(inventory, dresser, []);
        var reduced = cache.Get([Copy(2000)], [Entry(3000)], []);
        var replaced = cache.Get(inventory, dresser, []);

        Assert.Equal(1824, full.Visible.Count);
        Assert.Equal([2000u], reduced.All.InventoryDuplicates.Select(copy => copy.ItemId));
        Assert.Equal([3000u], reduced.All.DresserEntries.Select(entry => entry.ItemId));
        Assert.Equal(1824, replaced.Visible.Count);
        Assert.Equal(inventory, replaced.Visible.InventoryDuplicates);
        Assert.Equal(dresser, replaced.Visible.DresserEntries);
    }

    [Fact]
    public void NullInputsAreRejectedBeforeReturningCachedState()
    {
        var cache = new DuplicateReviewCache();
        cache.Get([], [], []);
        Assert.Equal("inventoryDuplicates",
            Assert.Throws<ArgumentNullException>(() => cache.Get(null!, null!, null!)).ParamName);
        Assert.Equal("dresserEntries",
            Assert.Throws<ArgumentNullException>(() => cache.Get([], null!, null!)).ParamName);
        Assert.Equal("ignoredItemIds",
            Assert.Throws<ArgumentNullException>(() => cache.Get([], [], null!)).ParamName);
    }

    private static InventoryDuplicateItem Copy(uint id) => new(0, (int)id, id, false,
        $"Copy {id}", 0, new(true, true, "Ready"), false, true, InventoryDuplicateDisposition.RemovalReview);

    private static DresserAlertEntry Entry(uint id) => new((int)id, id, id, false, false, 1,
        "Gear", $"Entry {id}", 0, DresserAlertKind.Duplicate, false, false, false);
}
