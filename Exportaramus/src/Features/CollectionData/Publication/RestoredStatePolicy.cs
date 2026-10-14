using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Features.CollectionData.Publication;

public static class RestoredStatePolicy
{
    public static CollectionState Reconcile(CollectionCatalog catalog, CollectionState saved,
        ImmutableHashSet<string> trustedSources, ImmutableArray<ReportedEvidence> reports, bool evidenceHealthy,
        DateTimeOffset now, long generation)
    {
        CollectionSourceStatus Health(string key, CollectionSourceStatus prior, bool mapped)
        {
            if (!trustedSources.Contains(key))
                return CaptureReadPolicy.Health(prior.DataType, generation, now, ReasonCode.PatchValidationRequired, false);
            if (!mapped) return CaptureReadPolicy.Health(prior.DataType, generation, now, ReasonCode.MappingValidationFailed, false);
            if (!prior.IsUsable) return CaptureReadPolicy.Health(prior.DataType, generation, now, ReasonCode.NotLoadedThisSession);
            return CollectionStatePolicy.Stale(prior, ReasonCode.RestoredFromDisk, "Restored from disk.", true) with
            {
                CapabilityState = CapabilityState.Supported,
                // Generations belong to a provider session. Prior-session generation numbers cannot exceed this session's root.
                CaptureGeneration = generation,
            };
        }
        var unlocks = saved.Unlocks.ToImmutableDictionary(pair => pair.Key, pair =>
        {
            var mapped = catalog.Collections.TryGetValue(pair.Key, out var definition)
                && pair.Value.UnlockedIds.All(definition.Items.Select(item => item.Id).ToImmutableHashSet().Contains);
            var health = Health(pair.Key, pair.Value.Health, mapped);
            return new PermanentUnlockState(health.IsUsable ? pair.Value.UnlockedIds : [], health);
        });
        var presence = saved.CurrentItemPresence.ToImmutableDictionary(pair => pair.Key, pair =>
        {
            var health = Health(pair.Key, pair.Value.Health, pair.Value.Items.All(item => catalog.KnownItemIds.Contains(item.ItemId)));
            return pair.Value with { Items = health.IsUsable ? pair.Value.Items : [], DresserEntries = health.IsUsable ? pair.Value.DresserEntries : null, Health = health };
        });
        var evidenceHealth = evidenceHealthy
            ? new CollectionSourceStatus(DataType.ReportedOwnership, SourceStatus.Fresh, Completeness.Complete, ProvenanceType.Reported,
                CapabilityState.Supported, ReasonCode.None, "", now, generation)
            : new(DataType.ReportedOwnership, SourceStatus.Unavailable, Completeness.Unknown, ProvenanceType.None,
                CapabilityState.Supported, ReasonCode.CaptureFailed, "Stored evidence could not be read.", null, generation);
        return saved with
        {
            CaptureGeneration = generation,
            CaptureType = CaptureType.Restored,
            CapturedAtUtc = now,
            Unlocks = unlocks,
            CurrentItemPresence = presence,
            Quests = [],
            ReportedEvidence = new(evidenceHealthy ? reports : [], evidenceHealth),
        };
    }
}
