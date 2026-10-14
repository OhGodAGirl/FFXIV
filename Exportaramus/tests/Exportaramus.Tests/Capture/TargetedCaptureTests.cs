using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Tests.Snapshots;

namespace Exportaramus.Tests.Capture;

public sealed class TargetedCaptureTests
{
    [Fact]
    public void LegacyPortExplicitlyRejectsTargetedReadsInsteadOfFallingBackToAFullCapture()
    {
        using ISnapshotCapturePort port = new LegacyPort();
        Assert.False(port.SupportsTargetedCapture);
        Assert.Throws<NotSupportedException>(() => port.BeginTargeted(["orchestrions"], 123, 1, SnapshotFixture.Now));
        Assert.False(((LegacyPort)port).Began);
    }

    private sealed class LegacyPort : ISnapshotCapturePort
    {
        public bool Began;
        public CollectionCatalog? Catalog => null;
        public bool PrepareCatalog() => false;
        public void Begin(bool full, ulong contentId, long generation, DateTimeOffset now) => Began = true;
        public bool Advance() => false;
        public RawCaptureBatch? TakeResult() => null;
        public void ResetSession() { }
        public void Dispose() { }
    }

    [Fact]
    public void QuestProgressChangesAreObservedWithoutChangingCompletedUnlockIds()
    {
        var prior = SnapshotFixture.Create().State;
        prior = prior with { Unlocks = prior.Unlocks.SetItem("quests", prior.Unlocks["quests"] with { UnlockedIds = [] }), Quests = [new(1, QuestStatus.InProgress, 1)] };
        var raw = new RawCaptureBatch(ImmutableDictionary<string, PermanentUnlockState>.Empty.Add("quests", prior.Unlocks["quests"]), ImmutableDictionary<string, CurrentItemPresenceState>.Empty, [new(1, QuestStatus.InProgress, 2)]);
        var next = TargetedCaptureMerge.Apply(prior, raw, 2, SnapshotFixture.Now);
        Assert.Empty(next.Unlocks["quests"].UnlockedIds);
        Assert.Equal(new QuestState(1, QuestStatus.InProgress, 2), Assert.Single(next.Quests));
        Assert.True(TargetedCaptureChangePolicy.HasChanges(prior, next, raw));
        var failed = raw with { Unlocks = raw.Unlocks.SetItem("quests", new([], CaptureReadPolicy.Health(DataType.PermanentUnlock, 3, SnapshotFixture.Now, ReasonCode.CaptureFailed, true))), Quests = [] };
        var retained = TargetedCaptureMerge.Apply(next, failed, 3, SnapshotFixture.Now);
        Assert.Equal(next.Quests, retained.Quests);
        Assert.Equal(SourceStatus.Stale, retained.Unlocks["quests"].Health.SourceStatus);
        var unsupported = failed with { Unlocks = failed.Unlocks.SetItem("quests", new([], CaptureReadPolicy.Health(DataType.PermanentUnlock, 4, SnapshotFixture.Now, ReasonCode.PatchValidationRequired, false))) };
        var invalidated = TargetedCaptureMerge.Apply(retained, unsupported, 4, SnapshotFixture.Now);
        Assert.Empty(invalidated.Quests);
        Assert.False(invalidated.Unlocks["quests"].Health.IsUsable);
        Assert.True(TargetedCaptureChangePolicy.HasChanges(retained, invalidated, unsupported));
    }

    [Fact]
    public void PatchIncompatibilityInvalidatesPriorUnlocksInsteadOfRetainingFalseSupport()
    {
        var prior = SnapshotFixture.Create().State;
        var raw = new RawCaptureBatch(ImmutableDictionary<string, PermanentUnlockState>.Empty.Add("orchestrions", new([], CaptureReadPolicy.Health(DataType.PermanentUnlock, 2, SnapshotFixture.Now, ReasonCode.PatchValidationRequired, false))), ImmutableDictionary<string, CurrentItemPresenceState>.Empty, []);
        var next = TargetedCaptureMerge.Apply(prior, raw, 2, SnapshotFixture.Now);
        Assert.Empty(next.Unlocks["orchestrions"].UnlockedIds);
        Assert.Equal(CapabilityState.UnavailablePendingValidation, next.Unlocks["orchestrions"].Health.CapabilityState);
        Assert.False(next.Unlocks["orchestrions"].Health.IsUsable);
        Assert.True(TargetedCaptureChangePolicy.HasChanges(prior, next, raw));
    }
    [Fact]
    public void DresserCompositionChangesRemainObservableEvenWithIdenticalAggregateMembership()
    {
        var prior = SnapshotFixture.Create().State;
        var original = prior.CurrentItemPresence["glamourDresser"] with { DresserEntries = [new(0, 100, 100, null, [new(0, 100)])] };
        prior = prior with { CurrentItemPresence = prior.CurrentItemPresence.SetItem("glamourDresser", original) };
        var same = original with { DresserEntries = [new(0, 100, 100, null, [new(0, 100)])] };
        var raw = new RawCaptureBatch(ImmutableDictionary<string, PermanentUnlockState>.Empty, ImmutableDictionary<string, CurrentItemPresenceState>.Empty.Add("glamourDresser", same), []);
        var next = TargetedCaptureMerge.Apply(prior, raw, 2, SnapshotFixture.Now);
        Assert.False(TargetedCaptureChangePolicy.HasChanges(prior, next, raw));
        var changed = same with { DresserEntries = [new(1, 100, 100, null, [new(0, 100)])] };
        next = next with { CurrentItemPresence = next.CurrentItemPresence.SetItem("glamourDresser", changed) };
        Assert.True(TargetedCaptureChangePolicy.HasChanges(prior, next, raw));
        next = next with { CurrentItemPresence = next.CurrentItemPresence.SetItem("glamourDresser", same with { DresserEntries = null }) };
        Assert.True(TargetedCaptureChangePolicy.HasChanges(prior, next, raw));
        foreach (var entries in new ImmutableArray<DresserEntry>[]
        {
            [], [new(0, 101, 100, null, [new(0, 100)])],
            [new(0, 100, 101, null, [new(0, 100)])],
            [new(0, 100, 100, 1, [new(0, 100)])],
            [new(0, 100, 100, null, [new(0, 101)])],
        })
            Assert.True(TargetedCaptureChangePolicy.HasChanges(prior,
                next with { CurrentItemPresence = next.CurrentItemPresence.SetItem("glamourDresser", same with { DresserEntries = entries }) }, raw));
    }
    [Fact]
    public void ObtainingAnItemDoesNotRegisterItAndRegistrationReadsOnlyItsCollection()
    {
        var fixture = SnapshotFixture.Create();
        var reader = new Reader();
        using var obtained = new CollectionReadOperation(reader, fixture.Catalog, false, 123, 2, SnapshotFixture.Now, sources: ["inventory"]);
        Assert.True(obtained.Advance());
        Assert.Empty(reader.UnlockReads); Assert.Equal(new[] { "inventory" }, reader.PresenceReads);
        Assert.Empty(obtained.Result!.Unlocks);
        var present = TargetedCaptureMerge.Apply(fixture.State, obtained.Result, 2, SnapshotFixture.Now);
        Assert.Same(fixture.State.Unlocks["orchestrions"], present.Unlocks["orchestrions"]);
        Assert.Equal(new PresenceItem(100, 5), Assert.Single(present.CurrentItemPresence["inventory"].Items));
        reader.Unlocked = false;
        using var registration = new CollectionReadOperation(reader, fixture.Catalog, false, 123, 3, SnapshotFixture.Now, sources: ["orchestrions"]);
        Assert.True(registration.Advance());
        Assert.Equal(new[] { "orchestrions" }, reader.UnlockReads);
        Assert.Empty(registration.Result!.CurrentItemPresence);
        Assert.Empty(registration.Result.Unlocks["orchestrions"].UnlockedIds);
    }

    [Fact]
    public void TargetedReadFailureRetainsOnlySameSourceEvidenceWithItsOldTimeAndFailureHealth()
    {
        var prior = SnapshotFixture.Create().State;
        var failure = new PermanentUnlockState([], CaptureReadPolicy.Health(DataType.PermanentUnlock, 2, SnapshotFixture.Now.AddSeconds(20), ReasonCode.CaptureFailed, true));
        var raw = new RawCaptureBatch(ImmutableDictionary<string, PermanentUnlockState>.Empty.Add("orchestrions", failure), ImmutableDictionary<string, CurrentItemPresenceState>.Empty, []);
        var next = TargetedCaptureMerge.Apply(prior, raw, 2, SnapshotFixture.Now.AddSeconds(20));
        Assert.Equal(new uint[] { 1 }, next.Unlocks["orchestrions"].UnlockedIds);
        Assert.Equal(SourceStatus.Stale, next.Unlocks["orchestrions"].Health.SourceStatus);
        Assert.Equal(ReasonCode.CaptureFailed, next.Unlocks["orchestrions"].Health.ReasonCode);
        Assert.Equal(prior.Unlocks["orchestrions"].Health.ObservedAtUtc, next.Unlocks["orchestrions"].Health.ObservedAtUtc);
        Assert.Same(prior.Unlocks["mounts"], next.Unlocks["mounts"]);
        Assert.Same(prior.CurrentItemPresence["inventory"], next.CurrentItemPresence["inventory"]);
        Assert.True(TargetedCaptureChangePolicy.HasChanges(prior, next, raw));
    }

    [Fact]
    public void UnchangedObservationsDoNotPublishTimestampChurnButChangedHealthAndQuantitiesDo()
    {
        var prior = SnapshotFixture.Create().State;
        var observation = prior.CurrentItemPresence["inventory"] with
        { Health = prior.CurrentItemPresence["inventory"].Health with { ObservedAtUtc = SnapshotFixture.Now.AddSeconds(2), CaptureGeneration = 2 } };
        var raw = new RawCaptureBatch(ImmutableDictionary<string, PermanentUnlockState>.Empty, ImmutableDictionary<string, CurrentItemPresenceState>.Empty.Add("inventory", observation), []);
        var next = TargetedCaptureMerge.Apply(prior, raw, 2, SnapshotFixture.Now.AddSeconds(2));
        Assert.False(TargetedCaptureChangePolicy.HasChanges(prior, next, raw));
        Assert.True(TargetedCaptureChangePolicy.HasChanges(prior, next with { CurrentItemPresence = next.CurrentItemPresence.SetItem("inventory", observation with { Items = [] }) }, raw));
        Assert.True(TargetedCaptureChangePolicy.HasChanges(prior, next with { CurrentItemPresence = next.CurrentItemPresence.SetItem("inventory", observation with { Health = observation.Health with { CapabilityState = CapabilityState.Degraded } }) }, raw));
        Assert.True(TargetedCaptureChangePolicy.HasChanges(prior, next with { CurrentItemPresence = next.CurrentItemPresence.SetItem("inventory", observation with { Health = observation.Health with { Reason = "Updated reader detail." } }) }, raw));
    }

    [Fact]
    public void IdentityLossDuringTargetedReadRejectsTheEntireBatch()
    {
        var reader = new Reader();
        using var operation = new CollectionReadOperation(reader, SnapshotFixture.Create().Catalog, false, 123, 2, SnapshotFixture.Now, sources: ["orchestrions"]);
        Assert.False(operation.Advance(1)); reader.Identity = false;
        Assert.True(operation.Advance()); Assert.True(operation.Rejected); Assert.Null(operation.Result);
    }

    private sealed class Reader : ICollectionReader
    {
        public bool Identity = true;
        public bool Unlocked = true;
        public List<string> UnlockReads { get; } = [];
        public List<string> PresenceReads { get; } = [];
        public bool IdentityMatches(ulong contentId) => Identity && contentId == 123;
        public bool IsAvailable(string key, string version) => true;
        public bool ReadUnlock(string key, CatalogItem row) { UnlockReads.Add(key); return Unlocked; }
        public QuestState ReadQuest(uint id, bool completed) => new(id, completed ? QuestStatus.Completed : QuestStatus.NotStarted, 0);
        public CurrentItemPresenceState ReadPresence(string source, CollectionCatalog catalog, ulong contentId, long generation, DateTimeOffset now)
        { PresenceReads.Add(source); return CaptureReadPolicy.Presence([new(100, 5)], true, true, generation, now); }
    }
}
