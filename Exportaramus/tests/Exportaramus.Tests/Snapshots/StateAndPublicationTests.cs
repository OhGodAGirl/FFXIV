using System.Collections.Immutable;
using System.Text.Json;
using Exportaramus.ConsumerKit.V1;
using Exportaramus.Features.CollectionData;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Snapshots;
namespace Exportaramus.Tests.Snapshots;

public sealed class StateAndPublicationTests
{
    [Fact]
    public void ActualOutputsPassBothReferenceValidators()
    {
        var data = SnapshotFixture.Create();
        var compact = CollectionDataSerializer.Compact(data, 1);
        var rich = CollectionDataSerializer.Rich(data, "Ala'm'n <雪>", SnapshotFixture.Now);
        var compactResult = CollectionDataSemanticValidator.ValidateSnapshot(compact, "123");
        Assert.True(compactResult.Success, compactResult.Code.ToString());
        var richResult = CollectionDataSemanticValidator.ValidateRich(rich);
        Assert.True(richResult.Success, richResult.Code.ToString());
        Assert.DoesNotContain("Ala'm", compact);
        using var parsed = JsonDocument.Parse(rich);
        Assert.Equal("Ala'm'n <雪>", parsed.RootElement.GetProperty("character").GetProperty("displayName").GetString());
        Assert.True(CollectionDataSemanticValidator.ValidateCapabilities(CapabilityDocumentWriter.Build(data, true)).Success);
    }

    [Fact]
    public void FreshPresenceReplacesOldIncludingQuantityDecrease()
    {
        var prior = new CurrentItemPresenceState([new(100, 8), new(101, 1)], true, SnapshotFixture.Health(DataType.CurrentItemPresence));
        var current = new CurrentItemPresenceState([new(100, 2)], true, prior.Health);
        Assert.Equal(current, CollectionStatePolicy.Resolve(prior, current, true, true));
        Assert.Empty(CollectionStatePolicy.Resolve(prior, current with { Items = [] }, true, true).Items);
    }

    [Theory]
    [InlineData(true, true, 2)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    public void RetentionRequiresSameCharacterAndAllowedSource(bool same, bool retain, int count)
    {
        var prior = new PermanentUnlockState([1, 2], SnapshotFixture.Health(DataType.PermanentUnlock));
        var missing = new PermanentUnlockState([999], SnapshotFixture.Health(DataType.PermanentUnlock, SourceStatus.Unavailable) with { ReasonCode = ReasonCode.WindowNotReady });
        var result = CollectionStatePolicy.Resolve(prior, missing, same, retain);
        Assert.Equal(count, result.UnlockedIds.Length);
        if (count != 0)
        {
            Assert.Equal(SourceStatus.Stale, result.Health.SourceStatus);
            Assert.Equal(prior.Health.ObservedAtUtc, result.Health.ObservedAtUtc);
            Assert.Equal(prior.Health.ProvenanceType, result.Health.ProvenanceType);
            Assert.Equal(missing.Health.ReasonCode, result.Health.ReasonCode);
        }
    }

    [Fact]
    public void MappingFailureInvalidatesRetainedPresence()
    {
        var prior = SnapshotFixture.Create().State.CurrentItemPresence["armoire"];
        var failed = prior with { Health = prior.Health with { SourceStatus = SourceStatus.Unavailable, ReasonCode = ReasonCode.MappingValidationFailed } };
        Assert.Empty(CollectionStatePolicy.Resolve(prior, failed, true, true).Items);
    }

    [Theory]
    [InlineData(ReasonCode.NotLoadedThisSession, true)]
    [InlineData(ReasonCode.WindowNotReady, true)]
    [InlineData(ReasonCode.IncompleteContainerSet, true)]
    [InlineData(ReasonCode.IncompleteRetainerSet, true)]
    [InlineData(ReasonCode.CaptureFailed, true)]
    [InlineData(ReasonCode.None, false)]
    [InlineData(ReasonCode.MappingValidationFailed, false)]
    [InlineData(ReasonCode.PatchValidationRequired, false)]
    [InlineData(ReasonCode.RestoredFromDisk, false)]
    [InlineData(ReasonCode.CharacterUnavailable, false)]
    [InlineData(ReasonCode.IdentityUnavailable, false)]
    [InlineData(ReasonCode.QuantityOverflow, false)]
    [InlineData(ReasonCode.GameDataVersionUnsupported, false)]
    [InlineData(ReasonCode.OutputSizeLimitExceeded, false)]
    [InlineData(ReasonCode.SerializerValidationFailed, false)]
    public void OnlyTemporaryReadFailuresRetainKnownOwnership(ReasonCode reason, bool expectedRetention)
    {
        var unlock = new PermanentUnlockState([1, 2], SnapshotFixture.Health(DataType.PermanentUnlock));
        var presence = new CurrentItemPresenceState([new(100, 8)], true, SnapshotFixture.Health(DataType.CurrentItemPresence));
        var unlockFailure = unlock with { UnlockedIds = [999], Health = unlock.Health with { SourceStatus = SourceStatus.Unavailable, ReasonCode = reason, Reason = "Current read failed." } };
        var presenceFailure = presence with { Items = [new(999, 1)], Health = presence.Health with { SourceStatus = SourceStatus.Unavailable, ReasonCode = reason, Reason = "Current read failed." } };

        var resolvedUnlock = CollectionStatePolicy.Resolve(unlock, unlockFailure, true, true);
        var resolvedPresence = CollectionStatePolicy.Resolve(presence, presenceFailure, true, true);

        Assert.Equal<uint>(expectedRetention ? [1, 2] : [], resolvedUnlock.UnlockedIds);
        Assert.Equal<Exportaramus.Features.CollectionData.State.PresenceItem>(expectedRetention ? [new(100, 8)] : [], resolvedPresence.Items);
        Assert.Equal(expectedRetention ? SourceStatus.Stale : SourceStatus.Unavailable, resolvedUnlock.Health.SourceStatus);
        Assert.Equal(expectedRetention ? SourceStatus.Stale : SourceStatus.Unavailable, resolvedPresence.Health.SourceStatus);
        Assert.Equal(reason, resolvedUnlock.Health.ReasonCode);
        Assert.Equal(reason, resolvedPresence.Health.ReasonCode);
    }

    [Fact]
    public void RestoreOnlyMatchingCharacterNeverRestoresReportsFromSnapshot()
    {
        var data = SnapshotFixture.Create();
        var state = data.State with { ReportedEvidence = data.State.ReportedEvidence with { Items = [new(100, "Test", SnapshotFixture.Now)] } };
        Assert.Throws<InvalidDataException>(() => CollectionStatePolicy.Restore(state, 124, Guid.NewGuid(), SnapshotFixture.Now));
        var result = CollectionStatePolicy.Restore(state, 123, Guid.NewGuid(), SnapshotFixture.Now);
        Assert.Empty(result.ReportedEvidence.Items);
        Assert.All(result.Unlocks.Values, value => Assert.Equal(SourceStatus.Stale, value.Health.SourceStatus));
        Assert.All(result.CurrentItemPresence.Values, value => Assert.Equal(ProvenanceType.Restored, value.Health.ProvenanceType));
    }

    [Fact]
    public void RestoringUnknownSourcesDoesNotFabricateAnObservation()
    {
        var data = SnapshotFixture.Create();
        var unknown = new PermanentUnlockState([], SnapshotFixture.Health(DataType.PermanentUnlock, SourceStatus.NeverObserved));
        var state = data.State with { Unlocks = data.State.Unlocks.SetItem("minions", unknown) };

        var restored = CollectionStatePolicy.Restore(state, 123, Guid.NewGuid(), SnapshotFixture.Now.AddMinutes(1));

        Assert.Equal(unknown, restored.Unlocks["minions"]);
        Assert.Equal(SourceStatus.Stale, restored.Unlocks["mounts"].Health.SourceStatus);
        Assert.Equal(ReasonCode.RestoredFromDisk, restored.Unlocks["mounts"].Health.ReasonCode);
        Assert.False(string.IsNullOrWhiteSpace(restored.Unlocks["mounts"].Health.Reason));
    }

    [Fact]
    public void TimestampOnlyRefreshKeepsFingerprintButChangesRevisionAndNotice()
    {
        var data = SnapshotFixture.Create();
        var updated = data with { State = data.State with { CapturedAtUtc = SnapshotFixture.Now.AddMinutes(1), CaptureGeneration = 2 } };
        var first = Bundle(data, 1); var second = Bundle(updated, 2);
        Assert.Equal(first.Fingerprint, second.Fingerprint); Assert.NotEqual(first.CompactJson, second.CompactJson);
        var coalescer = new ChangedNoticeCoalescer(); coalescer.Add(second, first, ["inventory"]);
        using var notice = JsonDocument.Parse(coalescer.Take()!);
        Assert.Equal("SourceHealth", notice.RootElement.GetProperty("changedDataTypes")[0].GetString());
        Assert.Single(notice.RootElement.GetProperty("changedDataTypes").EnumerateArray());
        Assert.Null(coalescer.Take());
    }

    [Fact]
    public void DerivedPresenceCanMoveAndThenDisappearWithoutManufacturingUnlock()
    {
        var data = SnapshotFixture.Create();
        var emptied = data.State.CurrentItemPresence.ToImmutableDictionary(pair => pair.Key, pair => pair.Value with { Items = [] });
        var state = data.State with { CurrentItemPresence = emptied, Unlocks = data.State.Unlocks.SetItem("achievements", data.State.Unlocks["achievements"] with { UnlockedIds = [] }) };
        var next = CollectionDataAssembler.Assemble(data.Catalog, state);
        Assert.False(next.Collections["outfits"].Items[0].Owned); Assert.False(next.Collections["relics"].Items[0].Owned);
        Assert.Equal(data.State.Unlocks["mounts"], next.State.Unlocks["mounts"]);
        var moved = state with { CurrentItemPresence = emptied.SetItem("inventory", data.State.CurrentItemPresence["inventory"]) };
        Assert.True(CollectionDataAssembler.Assemble(data.Catalog, moved).Collections["outfits"].Items[0].Owned);
    }

    [Fact]
    public void UnknownAbsenceOmitsDerivedCollection()
    {
        var data = SnapshotFixture.Create();
        var absent = data.State.CurrentItemPresence.ToImmutableDictionary(pair => pair.Key, pair => pair.Value with { Items = [] });
        absent = absent.SetItem("retainers", absent["retainers"] with { Health = SnapshotFixture.Health(DataType.CurrentItemPresence, SourceStatus.NeverObserved) });
        var result = CollectionDataAssembler.Assemble(data.Catalog, data.State with { CurrentItemPresence = absent });
        Assert.False(result.Collections.ContainsKey("outfits"));
        Assert.True(result.Collections["relics"].Items[0].Owned); // Exact achievement is positive evidence.
    }

    [Fact]
    public void NormalizationIsIdempotentAcrossGeneratedInputs()
    {
        var random = new Random(712);
        for (var i = 0; i < 300; i++)
        {
            var input = Enumerable.Range(0, 200).Select(_ => (uint)random.Next(50));
            var normalized = CollectionStatePolicy.NormalizeIds(input);
            Assert.Equal<uint>(normalized, CollectionStatePolicy.NormalizeIds(normalized)); Assert.DoesNotContain(0u, normalized);
            Assert.Equal(normalized.Order().Distinct(), normalized);
        }
    }

    public static AcceptedSnapshotBundle Bundle(CollectionData data, long revision)
    {
        var fingerprints = CollectionFingerprint.Sources(data);
        return new(data, revision, CollectionDataSerializer.Compact(data, revision), null, CollectionFingerprint.Combined(fingerprints), fingerprints,
            PreparedFFXIVCollectExport.Unavailable("Test"), 0, null);
    }
}
