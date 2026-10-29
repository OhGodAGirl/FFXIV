using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Tests.Capture;

public sealed class ReaderCompatibilityTests
{
    [Theory]
    [InlineData("2026.08.11.0000.0000")]
    [InlineData("2026.09.01.0000.0000")]
    [InlineData("2026.10.06.0000.0000")]
    [InlineData("2026.10.16.0000.0000")]
    public void ReviewedBuildEnablesTheExplicitReadersButNotUnknownMappings(string version)
    {
        string[] expected = ["mounts", "minions", "emotes", "bardings", "cards", "fashions", "frames", "occult_records",
            "orchestrions", "spells", "chocobo_routes", "leves", "hairstyles", "achievements",
            "aetherytes", "npcs", "quests", "survey_records", "inventory", "armoryChest",
            "chocoboSaddlebag", "premiumSaddlebag", "retainers", "armoire", "glamourDresser"];
        var sources = ReaderCompatibility.SourcesFor(version);
        Assert.Equal((version != "2026.08.11.0000.0000" ? expected.Append("facewear").Append("field_records").Append("beasts") : expected).Order(StringComparer.Ordinal), sources.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("Mounts", sources);
        Assert.Equal(version != "2026.08.11.0000.0000", sources.Contains("field_records"));
        Assert.DoesNotContain("unknown", sources);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026.10.06.0000.0001")]
    [InlineData("2026.10.16.0000.0001")]
    [InlineData("2026.10.06.0000.0000 ")]
    [InlineData("2026.10.16.0000.0000 ")]
    [InlineData("2026.10.06")]
    [InlineData("2026.10.16")]
    [InlineData("2026.09.01.0000.0001")]
    [InlineData("2026.09.01.0000.0000 ")]
    [InlineData("2026.08.11.0000.0001")]
    [InlineData("2026.08.11.0000.0000 ")]
    [InlineData("2026.08.11")]
    public void UnreviewedDataVersionsCannotInheritReaderEligibility(string version) =>
        Assert.Empty(ReaderCompatibility.SourcesFor(version));

    [Fact]
    public void FirstCaptureQueriesEligibleReadersAndDistinguishesCompleteEmptyFromUnavailable()
    {
        var definitions = new[]
        {
            new CollectionDefinition("mounts", "Mounts", DataType.PermanentUnlock, true, [new(1, 1, "Owned", false), new(2, 2, "Missing", false)]),
            new CollectionDefinition("minions", "Minions", DataType.PermanentUnlock, true, [new(2, 2, "Missing", false)]),
            new CollectionDefinition("field_records", "Field Records", DataType.PermanentUnlock, true, [new(1, 1, "Unknown", false)])
        };
        var catalog = new CollectionCatalog("English", "2026.08.11.0000.0000", definitions.ToImmutableDictionary(x => x.Key), []);
        var reader = new Reader();
        using var operation = new CollectionReadOperation(reader, catalog, true, 123, 1, DateTimeOffset.UtcNow);
        Assert.True(operation.Advance(128));
        Assert.Equal(3, reader.Reads);
        var result = Assert.IsType<RawCaptureBatch>(operation.Result);
        Assert.Equal(new uint[] { 1 }, result.Unlocks["mounts"].UnlockedIds);
        Assert.Empty(result.Unlocks["minions"].UnlockedIds);
        Assert.Equal(SourceStatus.Fresh, result.Unlocks["minions"].Health.SourceStatus);
        Assert.Equal(Completeness.Complete, result.Unlocks["minions"].Health.Completeness);
        Assert.Equal(CapabilityState.Supported, result.Unlocks["mounts"].Health.CapabilityState);
        Assert.Equal(CapabilityState.UnavailablePendingValidation, result.Unlocks["field_records"].Health.CapabilityState);
        Assert.Equal(Completeness.Unknown, result.Unlocks["field_records"].Health.Completeness);
        Assert.Null(result.Unlocks["field_records"].Health.ObservedAtUtc);
    }

    private sealed class Reader : ICollectionReader
    {
        public int Reads { get; private set; }
        public bool IdentityMatches(ulong contentId) => contentId == 123;
        public bool IsAvailable(string key, string version) => ReaderCompatibility.SourcesFor(version).Contains(key);
        public bool ReadUnlock(string key, CatalogItem row) { Assert.NotEqual("field_records", key); Reads++; return row.Id == 1; }
        public QuestState ReadQuest(uint id, bool completed) => throw new InvalidOperationException();
        public CurrentItemPresenceState ReadPresence(string source, CollectionCatalog catalog, ulong contentId, long generation, DateTimeOffset now) =>
            CaptureReadPolicy.FailedPresence(true, generation, now, ReasonCode.IncompleteContainerSet);
    }
}
