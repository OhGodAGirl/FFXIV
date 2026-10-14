using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Capture.Retainers;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Diagnostics;

namespace Exportaramus.Tests.Capture;

public sealed class CaptureTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-06T00:00:00Z");

    [Fact]
    public void CountedPresenceNormalizesWithoutRetainingPriorOrOverflowing()
    {
        var read = CaptureReadPolicy.Presence([new(9, 2), new(3, 1), new(9, 3), new(0, 0)], true, true, 4, Now);
        Assert.Equal(new[] { new PresenceItem(3, 1), new PresenceItem(9, 5) }, read.Items.ToArray());
        Assert.True(read.Health.IsUsable); Assert.Equal(Now, read.Health.ObservedAtUtc);
        Assert.Equal(ReasonCode.None, read.Health.ReasonCode);
        var empty = CaptureReadPolicy.Presence([], true, true, 5, Now);
        Assert.Empty(empty.Items); Assert.Equal(SourceStatus.Fresh, empty.Health.SourceStatus);
        var overflow = CaptureReadPolicy.Presence([new(9, uint.MaxValue), new(9, 1)], true, true, 6, Now);
        Assert.Equal(ReasonCode.QuantityOverflow, overflow.Health.ReasonCode); Assert.Empty(overflow.Items);
        var maximum = CaptureReadPolicy.Presence([new(9, uint.MaxValue)], true, true, 6, Now);
        Assert.Equal(uint.MaxValue, maximum.Items[0].Quantity);
    }

    [Fact]
    public void MembershipOmitsQuantitiesAndRejectsIncompleteObservations()
    {
        var set = CaptureReadPolicy.Presence([new(4, null), new(4, 23)], false, true, 1, Now);
        Assert.Equal(new PresenceItem(4, null), Assert.Single(set.Items)); Assert.False(set.SupportsQuantity);
        var incomplete = CaptureReadPolicy.Presence([new(4, 1)], true, false, 1, Now);
        Assert.Equal(ReasonCode.IncompleteContainerSet, incomplete.Health.ReasonCode); Assert.Null(incomplete.Health.ObservedAtUtc);
        foreach (uint? bad in new uint?[] { null, 0 })
        {
            var failure = CaptureReadPolicy.Presence([new(4, bad)], true, true, 1, Now);
            Assert.Equal(ReasonCode.CaptureFailed, failure.Health.ReasonCode); Assert.Empty(failure.Items);
        }
        var tooMany = CaptureReadPolicy.Presence(Enumerable.Range(1, 50001).Select(i => new PresenceItem((uint)i, 1)), true, true, 1, Now);
        Assert.Equal(ReasonCode.OutputSizeLimitExceeded, tooMany.Health.ReasonCode);
        var pending = CaptureReadPolicy.FailedPresence(false, 1, Now, ReasonCode.PatchValidationRequired, false);
        Assert.Equal(CapabilityState.UnavailablePendingValidation, pending.Health.CapabilityState);
        Assert.Equal(ProvenanceType.None, pending.Health.ProvenanceType); Assert.False(pending.Health.IsUsable);
    }

    [Fact]
    public void RetainersRequireEveryExpectedIdentityAndReplaceEachObservation()
    {
        var accumulator = new RetainerAccumulator();
        var first = accumulator.Observe(1, [10, 20], 10, Presence(7, 3), 1, Now);
        Assert.Equal(ReasonCode.IncompleteRetainerSet, first.Health.ReasonCode); Assert.Empty(first.Items);
        var complete = accumulator.Observe(1, [20, 10], 20, Presence(7, 2), 2, Now);
        Assert.Equal(new PresenceItem(7, 5), Assert.Single(complete.Items));
        var removed = accumulator.Observe(1, [10, 20], 10, CaptureReadPolicy.Presence([], true, true, 3, Now), 3, Now);
        Assert.Equal(new PresenceItem(7, 2), Assert.Single(removed.Items));
        var changed = accumulator.Observe(1, [10, 30], 30, Presence(8, 1), 4, Now);
        Assert.Empty(changed.Items); Assert.Equal(ReasonCode.IncompleteRetainerSet, changed.Health.ReasonCode);
        var replacement = accumulator.Observe(1, [10, 30], 10, Presence(9, 1), 5, Now);
        Assert.Equal(new uint[] { 8, 9 }, replacement.Items.Select(i => i.ItemId));
        var switched = accumulator.Observe(2, [10, 30], 10, Presence(50, 1), 6, Now);
        Assert.Empty(switched.Items);
        accumulator.Reset();
        Assert.Equal(ReasonCode.IncompleteRetainerSet, accumulator.Observe(2, [10, 30], 30, Presence(60, 1), 7, Now).Health.ReasonCode);
    }

    [Fact]
    public void InvalidOrUnreadableRetainerListsNeverBecomeEmptyFresh()
    {
        var accumulator = new RetainerAccumulator();
        foreach (var list in new[] { default(ImmutableArray<ulong>), ImmutableArray.Create(0ul), ImmutableArray.Create(1ul, 1ul), Enumerable.Range(1, 11).Select(i => (ulong)i).ToImmutableArray() })
            Assert.Equal(ReasonCode.IncompleteRetainerSet, accumulator.Observe(1, list, 0, null, 1, Now).Health.ReasonCode);
        Assert.Equal(ReasonCode.IncompleteRetainerSet, accumulator.Observe(0, [], 0, null, 1, Now).Health.ReasonCode);
        Assert.Empty(accumulator.Observe(1, [], 0, null, 1, Now).Items);
        Assert.Equal(ReasonCode.IncompleteRetainerSet, accumulator.Observe(1, [10], 99, Presence(1, 1), 1, Now).Health.ReasonCode);
        Assert.Equal(ReasonCode.IncompleteRetainerSet, accumulator.Observe(1, [10], 10, null, 1, Now).Health.ReasonCode);
        Assert.Equal(ReasonCode.IncompleteRetainerSet, accumulator.Observe(1, [10], 10, CaptureReadPolicy.FailedPresence(true, 1, Now, ReasonCode.CaptureFailed), 1, Now).Health.ReasonCode);
        var stale = Presence(1, 1); stale = stale with { Health = stale.Health with { SourceStatus = SourceStatus.Stale } };
        Assert.Equal(ReasonCode.IncompleteRetainerSet, accumulator.Observe(1, [10], 10, stale, 1, Now).Health.ReasonCode);
        Assert.Equal(ReasonCode.IncompleteRetainerSet, accumulator.Observe(1, [10], 10, Presence(1, 1) with { Health = null! }, 1, Now).Health.ReasonCode);
    }

    [Fact]
    public void CatalogCacheScopesExactLanguageAndVersion()
    {
        var cache = new CollectionCatalogCache(); Assert.Null(cache.Get("English", "one"));
        var catalog = Catalog(); cache.Set(catalog);
        Assert.Same(catalog, cache.Get("English", "one")); Assert.Null(cache.Get("French", "one")); Assert.Null(cache.Get("English", "two"));
        cache.Invalidate(); Assert.Null(cache.Get("English", "one"));
    }

    [Fact]
    public void IncrementalCaptureCompletesAndPendingSourcesNeverRead()
    {
        var fake = new FakeReader();
        using var operation = new CollectionReadOperation(fake, Catalog(), true, 1, 1, Now);
        Assert.False(operation.Advance(1));
        bool completed = false;
        for (var step = 0; step < 2 + 1 + CollectionReadOperation.PhysicalSources.Length + 1 && !completed; step++) completed = operation.Advance(1);
        Assert.True(completed, "Two unlock rows, one collection result, seven physical sources and completion must finish.");
        Assert.Equal(new uint[] { 1 }, operation.Result!.Unlocks["mounts"].UnlockedIds);
        Assert.Equal(7, operation.Result.CurrentItemPresence.Count); Assert.True(operation.Advance());
        Assert.Equal(2, fake.UnlockReads);
        fake = new FakeReader { Validated = false };
        using var pending = new CollectionReadOperation(fake, Catalog(), true, 1, 1, Now);
        Assert.False(pending.Advance(1)); Assert.Equal(0, fake.PresenceReads);
        Assert.True(pending.Advance()); Assert.Empty(pending.Result!.Unlocks["mounts"].UnlockedIds); Assert.Equal(0, fake.UnlockReads);
        Assert.Equal(CapabilityState.UnavailablePendingValidation, pending.Result.Unlocks["mounts"].Health.CapabilityState);
        var health = pending.Result.Unlocks["mounts"].Health;
        Assert.Equal(SourceStatus.Unavailable, health.SourceStatus);
        Assert.Equal(Completeness.Unknown, health.Completeness);
        Assert.Equal(ProvenanceType.None, health.ProvenanceType);
        Assert.Equal(ReasonCode.PatchValidationRequired, health.ReasonCode);
        Assert.Null(health.ObservedAtUtc);
    }

    [Fact]
    public void CaptureRejectsIdentityChangesAndDoesNotLeakPartialRows()
    {
        var fake = new FakeReader(); using var operation = new CollectionReadOperation(fake, Catalog(), true, 1, 1, Now);
        operation.Advance(1); fake.Identity = false;
        Assert.True(operation.Advance()); Assert.True(operation.Rejected); Assert.Null(operation.Result);
        fake.Identity = true;
        Assert.True(operation.Advance()); Assert.Null(operation.Result);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollectionReadOperation(new FakeReader(), Catalog(), true, 1, 1, Now).Advance(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollectionReadOperation(new FakeReader(), Catalog(), true, 1, 1, Now).Advance(2049));
        using var disposed = new CollectionReadOperation(fake, Catalog(), true, 1, 1, Now); disposed.Dispose(); Assert.True(disposed.Advance());
        var finalReader = new FakeReader { IdentityCallsBeforeLoss = 8 };
        using var finalIdentity = new CollectionReadOperation(finalReader, Catalog(), false, 1, 1, Now);
        Assert.True(finalIdentity.Advance()); Assert.True(finalIdentity.Rejected); Assert.Null(finalIdentity.Result);
        finalReader.IdentityCallsBeforeLoss = int.MaxValue;
        Assert.True(finalIdentity.Advance()); Assert.Null(finalIdentity.Result);
    }

    [Fact]
    public void RowFailuresOmitEntireCollectorAndPreserveOtherSources()
    {
        foreach (var exception in new Exception[] { new InvalidOperationException(), new SourceReadException(ReasonCode.NotLoadedThisSession) })
        {
            var reader = new FakeReader { Failure = exception };
            var measurements = new DiagnosticMeasurements();
            using var operation = new CollectionReadOperation(reader, Catalog(), true, 1, 1, Now, measurements);
            Assert.True(operation.Advance()); Assert.Empty(operation.Result!.Unlocks["mounts"].UnlockedIds);
            Assert.Equal(exception is SourceReadException ? ReasonCode.NotLoadedThisSession : ReasonCode.CaptureFailed, operation.Result.Unlocks["mounts"].Health.ReasonCode);
            Assert.Equal(7, operation.Result.CurrentItemPresence.Count);
            Assert.Equal(1, reader.UnlockReads);
            var failure = Assert.Single(measurements.Failures);
            Assert.Equal("Capture", failure.Component); Assert.Equal("mounts", failure.Operation);
            Assert.Equal(operation.Result.Unlocks["mounts"].Health.ReasonCode.ToString(), failure.Code);
            Assert.True(measurements.Snapshot().ContainsKey("Source.mounts"));
        }
    }

    [Fact]
    public void LightweightSkipsUnlocksAndFullCapturesQuestProgress()
    {
        var fake = new FakeReader(); using var light = new CollectionReadOperation(fake, Catalog(), false, 1, 1, Now);
        Assert.True(light.Advance()); Assert.Empty(light.Result!.Unlocks); Assert.Equal(0, fake.UnlockReads);
        Assert.Equal(new[] { "armoire", "armoryChest", "chocoboSaddlebag", "glamourDresser", "inventory", "premiumSaddlebag", "retainers" }, light.Result.CurrentItemPresence.Keys.Order());
        using var quests = new CollectionReadOperation(fake, Catalog("quests"), true, 1, 1, Now);
        Assert.True(quests.Advance()); Assert.Equal(2, quests.Result!.Quests.Length);
        Assert.Equal(QuestStatus.Completed, quests.Result.Quests[0].Status); Assert.Equal(QuestStatus.InProgress, quests.Result.Quests[1].Status);
        using var derived = new CollectionReadOperation(fake, Catalog("outfits", DataType.Derived), true, 1, 1, Now);
        Assert.True(derived.Advance()); Assert.Empty(derived.Result!.Unlocks);
    }

    [Fact]
    public void UnlockCardinalityLimitRejectsInsteadOfTruncating()
    {
        var catalog = Catalog();
        catalog = catalog with
        {
            Collections = catalog.Collections.SetItem("mounts", catalog.Collections["mounts"] with
            { Items = Enumerable.Range(1, 50001).Select(i => new CatalogItem((uint)i, (uint)i, "Fixture", false)).ToImmutableArray() })
        };
        using var operation = new CollectionReadOperation(new FakeReader { AllUnlocked = true }, catalog, true, 1, 1, Now);
        bool completed = false;
        var maximumSlices = (50001 + 1 + CollectionReadOperation.PhysicalSources.Length + 2047) / 2048 + 1;
        for (var slice = 0; slice < maximumSlices && !completed; slice++) completed = operation.Advance(2048);
        Assert.True(completed, "Capture must finish within the known catalog work budget.");
        Assert.Empty(operation.Result!.Unlocks["mounts"].UnlockedIds);
        Assert.Equal(ReasonCode.OutputSizeLimitExceeded, operation.Result.Unlocks["mounts"].Health.ReasonCode);
    }

    private static CurrentItemPresenceState Presence(uint id, uint count) => CaptureReadPolicy.Presence([new(id, count)], true, true, 1, Now);

    [Fact]
    public void WindowEdgesCoalesceAndIgnoreGuidedOwnedChanges()
    {
        var edge = new StorageWindowEdgePolicy();
        Assert.True(edge.IsDue(0)); Assert.True(edge.Observe(true, 4, 0, false));
        Assert.False(edge.Observe(true, 5, .9, false)); Assert.False(edge.IsDue(.9));
        Assert.True(edge.Observe(true, 5, 1, false)); Assert.False(edge.Observe(true, 5, 2, false));
        Assert.False(edge.Observe(false, 0, 3, false)); Assert.True(edge.Observe(true, 5, 4, false));
        Assert.False(edge.Observe(true, 6, 5, true)); Assert.False(edge.Observe(true, 6, 6, false));
        edge.Reset(); Assert.True(edge.Observe(true, 6, 7, false));
    }

    [Fact]
    public void DresserExpansionUsesOnlyNonzeroUnlockedSlotsAndRequiresExactMapping()
    {
        Assert.Equal(new uint[] { 3, 8 }, DresserSetExpansionPolicy.Expand([0, 8, 3, 8, 4, 0, 0, 0, 0, 0, 0],
            [true, true, true, true, false, false, false, false, false, false, false]).ToArray());
        foreach (var pair in new[] { (default(ImmutableArray<uint>), ImmutableArray.CreateRange(new bool[11])),
            (ImmutableArray.CreateRange(new uint[11]), default(ImmutableArray<bool>)),
            (ImmutableArray.CreateRange(new uint[10]), ImmutableArray.CreateRange(new bool[11])),
            (ImmutableArray.CreateRange(new uint[11]), ImmutableArray.CreateRange(new bool[10])) })
            Assert.Throws<ArgumentException>(() => DresserSetExpansionPolicy.Expand(pair.Item1, pair.Item2));
    }
    private static CollectionCatalog Catalog(string key = "mounts", DataType type = DataType.PermanentUnlock) => new("English", "one",
        ImmutableDictionary<string, CollectionDefinition>.Empty.Add(key, new(key, key, type, true,
            [new(1, 1, "One", false), new(2, 2, "Two", false)])), [1, 2]);

    private sealed class FakeReader : ICollectionReader
    {
        public bool Identity = true;
        public int IdentityCallsBeforeLoss = int.MaxValue;
        public bool Validated = true;
        public int UnlockReads;
        public int PresenceReads;
        public Exception? Failure;
        public bool AllUnlocked;
        public bool IdentityMatches(ulong contentId) => Identity && --IdentityCallsBeforeLoss >= 0;
        public bool IsAvailable(string key, string version) => Validated;
        public bool ReadUnlock(string key, CatalogItem row) { UnlockReads++; if (Failure != null) throw Failure; return AllUnlocked || row.Id == 1; }
        public QuestState ReadQuest(uint id, bool completed) => new(id, completed ? QuestStatus.Completed : QuestStatus.InProgress, completed ? null : (byte)1);
        public CurrentItemPresenceState ReadPresence(string source, CollectionCatalog catalog, ulong contentId, long generation, DateTimeOffset now)
        { PresenceReads++; return CaptureReadPolicy.Presence([], source is not ("armoire" or "glamourDresser"), true, generation, now); }
    }
}
