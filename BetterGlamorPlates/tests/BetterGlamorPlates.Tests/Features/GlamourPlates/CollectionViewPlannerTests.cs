namespace BetterGlamorPlates.Tests.Features.GlamourPlates;

using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Shared.CollectionData;

/// <summary>
/// Verifies canonical shared ownership is projected into a bounded,
/// deterministic collection view without inventing values for unknown sources.
/// </summary>
public sealed class CollectionViewPlannerTests
{
    /// <summary>Verifies the named policy scenario.</summary>
    [Fact]
    public void CreateRejectsNullFactLookup()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CollectionViewPlanner.Create(null, PlateSlot.Body, null, null!));
    }

    /// <summary>Verifies the named policy scenario.</summary>
    [Fact]
    public void MissingSnapshotProducesNoItemsAndNoKnownOwnedCount()
    {
        Assert.Empty(CollectionViewPlanner.Create(null, PlateSlot.Body, null, _ => null));
        Assert.Equal(0, CollectionViewPlanner.CountKnownOwnedItems(null));
    }

    /// <summary>Verifies the named policy scenario.</summary>
    [Fact]
    public void FreshEvidenceWinsStaleAndDresserWinsEqualFreshness()
    {
        var snapshot = TestDomainValues.SharedSnapshot(
            dresserItems: [51, 61, 62],
            armoireItems: [51, 61]);
        var staleDresser = snapshot with
        {
            GlamourDresser = snapshot.GlamourDresser with
            {
                Status = CollectionSourceStatus.Stale,
            },
        };
        var facts = Facts(51, 61, 62);

        var visible = CollectionViewPlanner.Create(
            staleDresser,
            PlateSlot.Body,
            null,
            itemId => facts.GetValueOrDefault(itemId));

        Assert.Equal(3, CollectionViewPlanner.CountKnownOwnedItems(staleDresser));
        Assert.Collection(
            visible.OrderBy(item => item.ItemId),
            item =>
            {
                Assert.Equal(51u, item.ItemId);
                Assert.Equal(CollectionSource.Armoire, item.Source);
                Assert.Equal(CollectionFreshness.Fresh, item.Freshness);
            },
            item =>
            {
                Assert.Equal(61u, item.ItemId);
                Assert.Equal(CollectionSource.Armoire, item.Source);
                Assert.Equal(CollectionFreshness.Fresh, item.Freshness);
            },
            item =>
            {
                Assert.Equal(62u, item.ItemId);
                Assert.Equal(CollectionSource.Dresser, item.Source);
                Assert.Equal(CollectionFreshness.LastKnown, item.Freshness);
            });
    }

    /// <summary>Verifies the named policy scenario.</summary>
    [Fact]
    public void EqualFreshnessPrefersDresserDeterministically()
    {
        var snapshot = TestDomainValues.SharedSnapshot(
            dresserItems: [51],
            armoireItems: [51]);
        var facts = Facts(51);

        var item = Assert.Single(CollectionViewPlanner.Create(
            snapshot,
            PlateSlot.Body,
            null,
            itemId => facts.GetValueOrDefault(itemId)));

        Assert.Equal(CollectionSource.Dresser, item.Source);
        Assert.Equal(CollectionFreshness.Fresh, item.Freshness);
    }

    /// <summary>Verifies duplicate values within one source remain one ownership entry.</summary>
    [Fact]
    public void DuplicateValuesWithinOneSourceRemainDeduplicated()
    {
        var snapshot = TestDomainValues.SharedSnapshot(
            dresserItems: [51, 51],
            armoireItems: []);
        var facts = Facts(51);

        var item = Assert.Single(CollectionViewPlanner.Create(
            snapshot,
            PlateSlot.Body,
            null,
            itemId => facts.GetValueOrDefault(itemId)));

        Assert.Equal(CollectionSource.Dresser, item.Source);
        Assert.Equal(1, CollectionViewPlanner.CountKnownOwnedItems(snapshot));
    }

    /// <summary>Verifies a fresh Dresser observation wins over a stale Armoire observation.</summary>
    [Fact]
    public void FreshDresserWinsStaleArmoire()
    {
        var snapshot = TestDomainValues.SharedSnapshot(
            glamourDresser: CollectionSourceStatus.Fresh,
            armoire: CollectionSourceStatus.Stale,
            dresserItems: [51],
            armoireItems: [51]);
        var facts = Facts(51);

        var item = Assert.Single(CollectionViewPlanner.Create(
            snapshot,
            PlateSlot.Body,
            null,
            itemId => facts.GetValueOrDefault(itemId)));

        Assert.Equal(CollectionSource.Dresser, item.Source);
        Assert.Equal(CollectionFreshness.Fresh, item.Freshness);
    }

    /// <summary>Verifies unknown sources ignore values even if a malformed snapshot carries them.</summary>
    [Fact]
    public void UnknownSourceStatusSuppressesUnexpectedValues()
    {
        var snapshot = TestDomainValues.SharedSnapshot(dresserItems: [], armoireItems: []) with
        {
            GlamourDresser = new CollectionSourceSnapshot(
                CollectionSourceStatus.Unavailable,
                null,
                [77U],
                "unavailable"),
        };

        Assert.Empty(CollectionViewPlanner.Create(
            snapshot,
            PlateSlot.Body,
            null,
            _ => throw new InvalidOperationException("Unknown ownership must not be enriched.")));
        Assert.Equal(0, CollectionViewPlanner.CountKnownOwnedItems(snapshot));
    }

    /// <summary>Verifies the named policy scenario.</summary>
    [Theory]
    [InlineData((int)CollectionSourceStatus.Unavailable)]
    [InlineData((int)CollectionSourceStatus.NeverObserved)]
    public void UnknownSourcesNeverBecomeEmptyOwnershipEvidence(int statusValue)
    {
        var status = (CollectionSourceStatus)statusValue;
        var snapshot = TestDomainValues.SharedSnapshot(
            glamourDresser: status,
            armoire: status,
            dresserItems: [],
            armoireItems: []);

        var visible = CollectionViewPlanner.Create(
            snapshot,
            PlateSlot.Body,
            null,
            _ => throw new InvalidOperationException("Unknown sources must not request item facts."));

        Assert.Empty(visible);
        Assert.Equal(0, CollectionViewPlanner.CountKnownOwnedItems(snapshot));
    }

    /// <summary>Verifies the named policy scenario.</summary>
    [Fact]
    public void CreateIsBoundedSortedFilteredAndSkipsUnknownItemFacts()
    {
        var ids = Enumerable.Range(1, 3_010).Select(value => (uint)value).ToArray();
        var snapshot = TestDomainValues.SharedSnapshot(
            dresserItems: ids,
            armoireItems: []);

        var visible = CollectionViewPlanner.Create(
            snapshot,
            PlateSlot.Body,
            "keep",
            itemId => itemId == 7
                ? null
                : new ItemDisplayFacts(
                    itemId % 3 == 0 ? $"keep {10_000 - itemId:D5}" : $"drop {itemId:D5}",
                    itemId,
                    itemId % 5 == 0 ? default : TestDomainValues.AllSlots));

        Assert.NotEmpty(visible);
        Assert.True(visible.Count <= CollectionViewPlanner.MaximumVisibleItems);
        Assert.All(visible, item =>
        {
            Assert.Contains("keep", item.Name, StringComparison.OrdinalIgnoreCase);
            Assert.True(PlateRules.MatchesSlot(item.SlotMask, PlateSlot.Body));
        });
        Assert.Equal(
            visible.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.ItemId),
            visible);
        Assert.DoesNotContain(visible, item => item.ItemId == 7);
        Assert.Equal(ids.Length, CollectionViewPlanner.CountKnownOwnedItems(snapshot));
    }

    /// <summary>Verifies item ID breaks ties between equal display names.</summary>
    [Fact]
    public void EqualNamesSortByAscendingItemId()
    {
        var snapshot = TestDomainValues.SharedSnapshot(
            dresserItems: [20, 10],
            armoireItems: []);

        var visible = CollectionViewPlanner.Create(
            snapshot,
            PlateSlot.Body,
            null,
            itemId => new ItemDisplayFacts("same", itemId, TestDomainValues.AllSlots));

        Assert.Equal([10U, 20U], visible.Select(item => item.ItemId));
    }

    /// <summary>Verifies the toolbar source toggles project only selected ownership sources.</summary>
    [Fact]
    public void SourceFilterCanShowEitherSourceOrNeither()
    {
        var snapshot = TestDomainValues.SharedSnapshot(
            dresserItems: [10],
            armoireItems: [20]);
        var facts = Facts(10, 20);

        var dresser = CollectionViewPlanner.Create(
            snapshot,
            PlateSlot.Body,
            null,
            CollectionSourceFilter.Dresser,
            itemId => facts.GetValueOrDefault(itemId));
        var armoire = CollectionViewPlanner.Create(
            snapshot,
            PlateSlot.Body,
            null,
            CollectionSourceFilter.Armoire,
            itemId => facts.GetValueOrDefault(itemId));
        var neither = CollectionViewPlanner.Create(
            snapshot,
            PlateSlot.Body,
            null,
            CollectionSourceFilter.None,
            itemId => facts.GetValueOrDefault(itemId));

        Assert.Equal(10u, Assert.Single(dresser).ItemId);
        Assert.Equal(20u, Assert.Single(armoire).ItemId);
        Assert.Empty(neither);
    }

    /// <summary>Verifies the named policy scenario.</summary>
    [Fact]
    public void GeneratedSnapshotsPreserveSourceStatusAndDeduplicationInvariants()
    {
        var random = new Random(77);
        for (var sample = 0; sample < 100; sample++)
        {
            var dresser = Enumerable.Range(0, random.Next(0, 250))
                .Select(_ => (uint)random.Next(1, 500))
                .Distinct()
                .Order()
                .ToArray();
            var armoire = Enumerable.Range(0, random.Next(0, 250))
                .Select(_ => (uint)random.Next(1, 500))
                .Distinct()
                .Order()
                .ToArray();
            var snapshot = TestDomainValues.SharedSnapshot(
                dresserItems: dresser,
                armoireItems: armoire,
                glamourDresser: sample % 3 == 0
                    ? CollectionSourceStatus.Stale
                    : CollectionSourceStatus.Fresh);
            var facts = dresser.Concat(armoire).Distinct().ToDictionary(
                itemId => itemId,
                itemId => new ItemDisplayFacts($"item {itemId}", itemId, TestDomainValues.AllSlots));

            var visible = CollectionViewPlanner.Create(
                snapshot,
                PlateSlot.Body,
                null,
                itemId => facts.GetValueOrDefault(itemId));

            Assert.Equal(visible.Count, visible.Select(item => item.ItemId).Distinct().Count());
            Assert.All(visible, item => Assert.Contains(item.ItemId, dresser.Concat(armoire)));
            Assert.True(visible.Count <= CollectionViewPlanner.MaximumVisibleItems);
        }
    }

    private static Dictionary<uint, ItemDisplayFacts> Facts(params uint[] itemIds)
        => itemIds.ToDictionary(
            itemId => itemId,
            itemId => new ItemDisplayFacts($"item {itemId}", itemId, TestDomainValues.AllSlots));
}
