using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Tests.Capture;

public sealed class FacewearMappingTests
{
    [Fact]
    public void StylesArePositiveDistinctAndZeroStyleIsIncluded()
    {
        var rows = new[] { FacewearMappingPolicy.Project(1, 0), FacewearMappingPolicy.Project(2, 0), FacewearMappingPolicy.Project(3, 7) };
        var catalog = CollectionCatalogBuilderPolicy.Build(new("English", ReaderCompatibility.CurrentGameVersion,
            CatalogRowPolicy.Definitions, rows.Select(r => ("facewear", r)).ToImmutableArray(), []));
        Assert.Equal(new uint[] { 1, 8 }, catalog.Collections["facewear"].Items.Select(r => r.Id));
    }

    [Fact]
    public void AnyUnlockedVariantOwnsTheStyleAndEveryVariantIsQueried()
    {
        var queried = new List<ushort>();
        Assert.True(FacewearMappingPolicy.Read(1, [new(0, 0), new(8, 0), new(9, 0), new(10, 1)],
            id => { queried.Add(id); return id == 9; }));
        Assert.Equal(new ushort[] { 8, 9 }, queried);
        Assert.False(FacewearMappingPolicy.Read(1, [new(8, 0), new(9, 0)], _ => false));
    }

    [Fact]
    public void MissingOrInvalidMappingsFailInsteadOfProvingAbsence()
    {
        Assert.Throws<SourceReadException>(() => FacewearMappingPolicy.Read(0, [new(1, 0)], _ => false));
        Assert.Throws<SourceReadException>(() => FacewearMappingPolicy.Read(1, [], _ => false));
        Assert.Throws<SourceReadException>(() => FacewearMappingPolicy.Read(1, [new(1, 0), new(65536, 0)], _ => true));
        Assert.Throws<SourceReadException>(() => FacewearMappingPolicy.Project(1, uint.MaxValue));
    }

    [Theory]
    [InlineData("2026.09.01.0000.0000", true)]
    [InlineData("2026.10.06.0000.0000", true)]
    [InlineData("2026.08.11.0000.0000", false)]
    [InlineData("2026.09.01.0000.0001", false)]
    public void EmptyOwnershipIsCompleteOnlyOnReviewedBuild(string version, bool supported)
    {
        var reader = new Reader();
        using var operation = Operation(reader, version);
        var completed = false;
        var maximumSteps = 1 + 1 + CollectionReadOperation.PhysicalSources.Length + 1;
        for (var step = 0; step < maximumSteps && !completed; step++) completed = operation.Advance(1);
        Assert.True(completed);
        var state = operation.Result!.Unlocks["facewear"];
        Assert.Empty(state.UnlockedIds);
        Assert.Equal(supported ? Completeness.Complete : Completeness.Unknown, state.Health.Completeness);
        Assert.Equal(supported ? 1 : 0, reader.Reads);
    }

    [Fact]
    public void IdentityChangeRejectsFacewearBatch()
    {
        var reader = new Reader();
        using var operation = Operation(reader, ReaderCompatibility.CurrentGameVersion);
        Assert.False(operation.Advance(1));
        reader.Identity = false;
        Assert.True(operation.Advance(1));
        Assert.True(operation.Rejected);
        Assert.Null(operation.Result);
    }

    private static CollectionReadOperation Operation(Reader reader, string version) => new(reader,
        new("English", version, new[] { new CollectionDefinition("facewear", "Facewear", DataType.PermanentUnlock, true,
            [FacewearMappingPolicy.Project(1, 0)]) }.ToImmutableDictionary(r => r.Key), []), true, 123, 1, DateTimeOffset.UtcNow);

    private sealed class Reader : ICollectionReader
    {
        public bool Identity = true;
        public int Reads;
        public bool IdentityMatches(ulong contentId) => Identity && contentId == 123;
        public bool IsAvailable(string key, string version) => ReaderCompatibility.SourcesFor(version).Contains(key);
        public bool ReadUnlock(string key, CatalogItem row) { Reads++; return false; }
        public QuestState ReadQuest(uint id, bool completed) => throw new InvalidOperationException();
        public CurrentItemPresenceState ReadPresence(string source, CollectionCatalog catalog, ulong contentId, long generation, DateTimeOffset now) =>
            CaptureReadPolicy.FailedPresence(true, generation, now, ReasonCode.IncompleteContainerSet);
    }
}
