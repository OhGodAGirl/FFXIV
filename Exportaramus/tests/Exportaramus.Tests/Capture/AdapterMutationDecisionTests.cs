using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Capture.Retainers;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Tests.Capture;

public sealed class AdapterMutationDecisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(50000, true)]
    [InlineData(50001, false)]
    public void PermanentUnlockLimitAcceptsExactlyFiftyThousandAndRejectsTheNextRow(int count, bool accepted)
    {
        var rows = Enumerable.Range(1, count).Select(id => new CatalogItem((uint)id, (uint)id, "Fixture", false)).ToImmutableArray();
        var catalog = new CollectionCatalog("English", "build",
            ImmutableDictionary<string, CollectionDefinition>.Empty.Add("mounts", new("mounts", "Mounts", DataType.PermanentUnlock, true, rows)), []);
        var reader = new Reader();
        using var operation = new CollectionReadOperation(reader, catalog, true, 1, 7, Now);

        Assert.False(operation.Advance(1));
        Assert.Equal(1, reader.UnlockReads);
        bool completed = false;
        var maximumSlices = (count + 1 + CollectionReadOperation.PhysicalSources.Length + 2047) / 2048 + 1;
        for (var slice = 0; slice < maximumSlices && !completed; slice++) completed = operation.Advance(2048);
        Assert.True(completed, "Capture must finish within its catalog and physical-source work budget.");

        var source = operation.Result!.Unlocks["mounts"];
        Assert.Equal(count, reader.UnlockReads);
        Assert.Equal(accepted ? ReasonCode.None : ReasonCode.OutputSizeLimitExceeded, source.Health.ReasonCode);
        Assert.Equal(accepted ? count : 0, source.UnlockedIds.Length);
        Assert.Equal(accepted, source.Health.IsUsable);
    }

    [Fact]
    public void TenRetainersCanBecomeCompleteOnlyAfterAllTenFreshObservations()
    {
        var accumulator = new RetainerAccumulator();
        var expected = Enumerable.Range(1, 10).Select(id => (ulong)id).ToImmutableArray();
        for (var index = 0; index < expected.Length; index++)
        {
            var result = accumulator.Observe(1, expected, expected[index], Presence(), 1, Now);
            Assert.True(result.SupportsQuantity);
            if (index < 9) { Assert.Empty(result.Items); Assert.Equal(ReasonCode.IncompleteRetainerSet, result.Health.ReasonCode); }
            else { Assert.Equal(new PresenceItem(100, 10), Assert.Single(result.Items)); Assert.True(result.Health.IsUsable); }
        }
    }

    [Fact]
    public void MixedValidAndZeroRetainerIdentityInvalidatesTheWholePreviouslyObservedSet()
    {
        var accumulator = new RetainerAccumulator();
        Assert.True(accumulator.Observe(1, [1], 1, Presence(), 1, Now).Health.IsUsable);

        var firstInvalid = accumulator.Observe(1, [1, 0], 1, Presence(), 2, Now);
        Assert.Empty(firstInvalid.Items); Assert.Equal(ReasonCode.IncompleteRetainerSet, firstInvalid.Health.ReasonCode);
        var invalid = accumulator.Observe(1, [1, 0], 0, Presence(), 2, Now);

        Assert.Empty(invalid.Items); Assert.Equal(ReasonCode.IncompleteRetainerSet, invalid.Health.ReasonCode);
        Assert.True(invalid.SupportsQuantity);
        var next = accumulator.Observe(1, [1], 0, null, 3, Now);
        Assert.Empty(next.Items); Assert.Equal(ReasonCode.IncompleteRetainerSet, next.Health.ReasonCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2048)]
    public void BothInclusiveProjectionSliceBoundsAreAccepted(int maximumRows)
    {
        using var builder = new BoundedCatalogProjection("English", "build",
            Enumerable.Empty<(string, CatalogItem)>().GetEnumerator(), () => ("English", "build"), () => 0);
        Assert.True(builder.Advance(maximumRows));
        Assert.NotNull(builder.Projection);
    }

    private static CurrentItemPresenceState Presence() => CaptureReadPolicy.Presence([new(100, 1)], true, true, 1, Now);

    private sealed class Reader : ICollectionReader
    {
        public int UnlockReads { get; private set; }
        public bool IdentityMatches(ulong contentId) => true;
        public bool IsAvailable(string key, string version) => true;
        public bool ReadUnlock(string key, CatalogItem row) { UnlockReads++; return true; }
        public QuestState ReadQuest(uint id, bool complete) => new(id, QuestStatus.Completed, null);
        public CurrentItemPresenceState ReadPresence(string source, CollectionCatalog catalog, ulong contentId, long generation, DateTimeOffset now) => Presence();
    }
}
