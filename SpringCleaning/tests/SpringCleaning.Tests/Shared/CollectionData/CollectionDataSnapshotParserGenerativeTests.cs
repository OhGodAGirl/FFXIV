using System.Text.Json.Nodes;
using SpringCleaning.Shared.CollectionData;

namespace SpringCleaning.Tests.CollectionData;

public sealed class CollectionDataSnapshotParserGenerativeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SeededSortedSnapshotsRoundTripAndPreserveCounts()
    {
        var random = new Random(unchecked((int)0xC011EC7));
        for (var iteration = 0; iteration < 250; iteration++)
        {
            var inventory = DistinctSorted(random, minimumCount: 0, maximumCountExclusive: 8);
            var armoire = DistinctSorted(random, minimumCount: 0, maximumCountExclusive: 5);
            var dresser = DistinctSorted(random, minimumCount: 0, maximumCountExclusive: 4);
            var root = CreateValidSnapshot();
            ProviderFixture.Items(root, "inventory", inventory);
            ProviderFixture.Items(root, "armoire", armoire);
            ProviderFixture.Items(root, "glamourDresser", dresser);
            var parsed = CollectionDataSnapshotParserTests.Parse(root);
            Assert.True(parsed.Accepted);
            Assert.Equal(inventory, parsed.Snapshot!.Inventory.ItemIds.ToArray());
            Assert.Equal(armoire, parsed.Snapshot.Armoire.ItemIds.ToArray());
            Assert.Equal(dresser, parsed.Snapshot.GlamourDresser.ItemIds.ToArray());
        }
    }

    [Fact]
    public void SeededDescendingOrDuplicateItemIdsAreRejected()
    {
        var random = new Random(unchecked((int)0xBAD1DEA));
        for (var iteration = 0; iteration < 250; iteration++)
        {
            var values = DistinctSorted(random, minimumCount: 2, maximumCountExclusive: 8);
            var root = CreateValidSnapshot();
            var invalid = values.ToArray();
            if (iteration % 2 == 0)
                invalid[0] = invalid[1];
            else
                Array.Reverse(invalid);
            ProviderFixture.Items(root, "inventory", invalid);

            var parsed = CollectionDataSnapshotParser.Parse(root.ToJsonString(), 42, Now);

            Assert.Equal(CollectionDataFailure.MalformedJson, parsed.Failure);
        }
    }

    private static uint[] DistinctSorted(
        Random random,
        int minimumCount,
        int maximumCountExclusive)
    {
        var values = new SortedSet<uint>();
        var targetCount = random.Next(minimumCount, maximumCountExclusive);
        while (values.Count < targetCount)
            values.Add((uint)random.Next(1, 500));

        return values.ToArray();
    }

    private static JsonObject CreateValidSnapshot() => ProviderFixture.Snapshot();
}
