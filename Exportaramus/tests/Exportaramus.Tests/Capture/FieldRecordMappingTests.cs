using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.CollectionData.Catalog;
using System.Collections.Immutable;

namespace Exportaramus.Tests.Capture;

public sealed class FieldRecordMappingTests
{
    private static (uint RecordId, uint ItemId)[] Rows() => Enumerable.Range(1, 50).Select(i => ((uint)i, (uint)(30000 + i))).ToArray();

    [Fact]
    public void FullMappingPreservesAllRecordNumbersAndTheirDistinctItemIds()
    {
        var map = FieldRecordMappingPolicy.CreateMap(Rows().Reverse());
        Assert.Equal(50, map.Count);
        foreach (var (record, item) in Rows()) Assert.Equal(item, map[record]);
    }

    [Theory]
    [InlineData(0, 30001)]
    [InlineData(51, 30001)]
    [InlineData(1, 0)]
    [InlineData(2, 30001)]
    [InlineData(1, 30002)]
    public void InvalidOrAmbiguousMappingCannotProduceOwnership(uint record, uint item)
    {
        var rows = Rows(); rows[0] = (record, item);
        Assert.Equal(ReasonCode.MappingValidationFailed,
            Assert.Throws<SourceReadException>(() => FieldRecordMappingPolicy.CreateMap(rows)).Code);
    }

    [Fact]
    public void MissingRecordCannotLookLikeAnEmptyCollection() => Assert.Equal(ReasonCode.MappingValidationFailed,
        Assert.Throws<SourceReadException>(() => FieldRecordMappingPolicy.CreateMap(Rows().Skip(1))).Code);

    [Theory]
    [InlineData(0, 40000)]
    [InlineData(51, 40000)]
    [InlineData(1, 0)]
    [InlineData(1, 40000)]
    [InlineData(1, 30001)]
    public void CompleteMappingCannotHideAnExtraInvalidOrDuplicateRow(uint record, uint item) =>
        Assert.Equal(ReasonCode.MappingValidationFailed,
            Assert.Throws<SourceReadException>(() => FieldRecordMappingPolicy.CreateMap(Rows().Append((record, item)))).Code);

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void OnlyExplicitNativeOwnershipStatesProduceABoolean(long result, bool owned) =>
        Assert.Equal(owned, FieldRecordMappingPolicy.ReadResult(result));

    [Fact]
    public void PendingServerDataIsNotUnowned() => Assert.Equal(ReasonCode.NotLoadedThisSession,
        Assert.Throws<SourceReadException>(() => FieldRecordMappingPolicy.ReadResult(3)).Code);

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    [InlineData(long.MaxValue)]
    public void UnsupportedNativeResponsesRemainUnknown(long result) => Assert.Equal(ReasonCode.MappingValidationFailed,
        Assert.Throws<SourceReadException>(() => FieldRecordMappingPolicy.ReadResult(result)).Code);

    [Theory]
    [InlineData(2, 0, Completeness.Complete)]
    [InlineData(1, 50, Completeness.Complete)]
    [InlineData(3, 0, Completeness.Unknown)]
    public void CaptureDistinguishesVerifiedEmptyOwnedAndPending(long response, int count, Completeness completeness)
    {
        var definition = new CollectionDefinition("field_records", "Field Records", DataType.PermanentUnlock, true,
            [.. Rows().Select(r => new CatalogItem(r.RecordId, r.RecordId, "Record", false))]);
        var catalog = new CollectionCatalog("English", ReaderCompatibility.CurrentGameVersion,
            ImmutableDictionary<string, CollectionDefinition>.Empty.Add(definition.Key, definition), []);
        using var operation = new CollectionReadOperation(new Reader(response), catalog, true, 1, 1, DateTimeOffset.UtcNow);
        Assert.True(operation.Advance());
        var result = Assert.IsType<RawCaptureBatch>(operation.Result).Unlocks["field_records"];
        Assert.Equal(count, result.UnlockedIds.Length);
        Assert.Equal(completeness, result.Health.Completeness);
        Assert.Equal(response != 3, result.Health.IsUsable);
    }

    private sealed class Reader(long response) : ICollectionReader
    {
        public bool IdentityMatches(ulong contentId) => true;
        public bool IsAvailable(string key, string version) => ReaderCompatibility.SourcesFor(version).Contains(key);
        public bool ReadUnlock(string key, CatalogItem row) => FieldRecordMappingPolicy.ReadResult(response);
        public QuestState ReadQuest(uint id, bool completed) => throw new NotSupportedException();
        public CurrentItemPresenceState ReadPresence(string source, CollectionCatalog catalog, ulong contentId, long generation, DateTimeOffset now) =>
            CaptureReadPolicy.FailedPresence(true, generation, now, ReasonCode.WindowNotReady);
    }
}
