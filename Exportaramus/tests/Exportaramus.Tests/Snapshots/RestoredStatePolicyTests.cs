using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Contracts;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Tests.Snapshots;

public sealed class RestoredStatePolicyTests
{
    [Theory]
    [InlineData(1, 123, 1, 123, false, true)]
    [InlineData(0, 123, 1, 123, false, false)]
    [InlineData(1, 124, 1, 123, false, false)]
    [InlineData(1, 123, 1, 123, true, false)]
    public void CompletionRequiresEveryIndependentScopeValue(long resultGeneration, ulong resultCharacter,
        long generation, ulong character, bool canceled, bool expected) =>
        Assert.Equal(expected, Exportaramus.Features.Snapshots.SnapshotCompletionPolicy.IsCurrent(resultGeneration, resultCharacter, generation, character, canceled));

    [Fact]
    public void CurrentTrustAndCurrentMappingsGateEveryRestoredObservation()
    {
        var fixture = SnapshotFixture.Create();
        var trusted = CollectionKeys.Unlocks.Concat(CollectionKeys.Presence).ToImmutableHashSet();
        var restored = RestoredStatePolicy.Reconcile(fixture.Catalog, fixture.State, trusted, [], true, SnapshotFixture.Now.AddHours(1), 0);
        Assert.All(restored.Unlocks.Values, source =>
        {
            Assert.Equal(SourceStatus.Stale, source.Health.SourceStatus); Assert.Equal(ProvenanceType.Restored, source.Health.ProvenanceType);
            Assert.Equal(0, source.Health.CaptureGeneration); Assert.Equal(SnapshotFixture.Now, source.Health.ObservedAtUtc);
        });
        Assert.All(restored.CurrentItemPresence.Values, source => Assert.Equal(SourceStatus.Stale, source.Health.SourceStatus));
        foreach (var pair in fixture.State.Unlocks)
        {
            Assert.Equal(pair.Value.UnlockedIds, restored.Unlocks[pair.Key].UnlockedIds);
            Assert.False(string.IsNullOrWhiteSpace(restored.Unlocks[pair.Key].Health.Reason));
        }
        foreach (var pair in fixture.State.CurrentItemPresence)
            Assert.Equal(pair.Value.Items, restored.CurrentItemPresence[pair.Key].Items);
        Assert.Equal(0, restored.CaptureGeneration); Assert.Equal(CaptureType.Restored, restored.CaptureType); Assert.Empty(restored.Quests);
        Assert.True(Exportaramus.ConsumerKit.V1.CollectionDataSemanticValidator.ValidateSnapshot(
            CollectionDataSerializer.Compact(CollectionDataAssembler.Assemble(fixture.Catalog, restored), 1), "123").Success);
        var untrusted = RestoredStatePolicy.Reconcile(fixture.Catalog with { GameDataVersion = "different build" }, fixture.State, [], [], true, SnapshotFixture.Now, 0);
        Assert.All(untrusted.Unlocks.Values, source => { Assert.Empty(source.UnlockedIds); Assert.Equal(CapabilityState.UnavailablePendingValidation, source.Health.CapabilityState); });
        Assert.All(untrusted.CurrentItemPresence.Values, source => { Assert.Empty(source.Items); Assert.Null(source.Health.ObservedAtUtc); });
    }

    [Fact]
    public void RemovedCatalogIdsAndUnknownItemsCannotBeRetainedEvenForTrustedCollectors()
    {
        var fixture = SnapshotFixture.Create();
        var trusted = CollectionKeys.Unlocks.Concat(CollectionKeys.Presence).ToImmutableHashSet();
        var catalog = fixture.Catalog with
        {
            Collections = fixture.Catalog.Collections.Remove("mounts").SetItem("minions", fixture.Catalog.Collections["minions"] with { Items = [] }),
            KnownItemIds = [],
        };
        var result = RestoredStatePolicy.Reconcile(catalog, fixture.State, trusted, [], true, SnapshotFixture.Now, 0);
        Assert.Equal(ReasonCode.MappingValidationFailed, result.Unlocks["mounts"].Health.ReasonCode);
        Assert.Equal(CapabilityState.UnavailablePendingValidation, result.Unlocks["mounts"].Health.CapabilityState);
        Assert.Equal(ReasonCode.MappingValidationFailed, result.Unlocks["minions"].Health.ReasonCode);
        Assert.Empty(result.Unlocks["minions"].UnlockedIds);
        Assert.All(result.CurrentItemPresence.Values, source => { Assert.Empty(source.Items); Assert.Equal(ReasonCode.MappingValidationFailed, source.Health.ReasonCode); });
        var neverObserved = CollectionStateFactory.Empty(123, Guid.NewGuid(), SnapshotFixture.Now);
        var unknown = RestoredStatePolicy.Reconcile(fixture.Catalog, neverObserved, trusted, [], true, SnapshotFixture.Now, 0);
        Assert.All(unknown.Unlocks.Values, source => { Assert.Empty(source.UnlockedIds); Assert.Equal(ReasonCode.NotLoadedThisSession, source.Health.ReasonCode); });
    }

    [Fact]
    public void ATrustedSourceWithMixedKnownAndRemovedItemsIsWhollyUnknown()
    {
        var fixture = SnapshotFixture.Create();
        var saved = fixture.State with
        {
            CurrentItemPresence = fixture.State.CurrentItemPresence.SetItem("inventory", fixture.State.CurrentItemPresence["inventory"] with { Items = [new(100, 1), new(999999, 1)] })
        };
        var result = RestoredStatePolicy.Reconcile(fixture.Catalog, saved, ["inventory"], [], true, SnapshotFixture.Now, 0);
        Assert.Empty(result.CurrentItemPresence["inventory"].Items);
        Assert.Equal(ReasonCode.MappingValidationFailed, result.CurrentItemPresence["inventory"].Health.ReasonCode);
        Assert.Equal(CapabilityState.UnavailablePendingValidation, result.CurrentItemPresence["inventory"].Health.CapabilityState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DedicatedEvidenceReadWhollyEstablishesReportedSourceHealth(bool healthy)
    {
        var fixture = SnapshotFixture.Create();
        var saved = fixture.State with
        {
            ReportedEvidence = new([], fixture.State.ReportedEvidence.Health with
            { SourceStatus = SourceStatus.Unavailable, Completeness = Completeness.Unknown, ProvenanceType = ProvenanceType.None, ObservedAtUtc = null })
        };
        var now = SnapshotFixture.Now.AddHours(1);
        var result = RestoredStatePolicy.Reconcile(fixture.Catalog, saved, [], [new(100, "Fixture", now)], healthy, now, 2);
        Assert.Equal(healthy ? SourceStatus.Fresh : SourceStatus.Unavailable, result.ReportedEvidence.Health.SourceStatus);
        Assert.Equal(healthy ? Completeness.Complete : Completeness.Unknown, result.ReportedEvidence.Health.Completeness);
        Assert.Equal(healthy ? ProvenanceType.Reported : ProvenanceType.None, result.ReportedEvidence.Health.ProvenanceType);
        Assert.Equal(healthy ? now : (DateTimeOffset?)null, result.ReportedEvidence.Health.ObservedAtUtc);
        Assert.Equal(2, result.ReportedEvidence.Health.CaptureGeneration);
        Assert.Equal(healthy ? 1 : 0, result.ReportedEvidence.Items.Length);
        if (healthy) Assert.Empty(result.ReportedEvidence.Health.Reason);
        else Assert.False(string.IsNullOrWhiteSpace(result.ReportedEvidence.Health.Reason));
    }
}
