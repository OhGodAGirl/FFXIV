using System.Collections.Immutable;
namespace Exportaramus.Features.CollectionData.State;

public static class CollectionStatePolicy
{
    public static PermanentUnlockState Resolve(PermanentUnlockState? prior, PermanentUnlockState observed, bool sameCharacter, bool retain)
    {
        if (observed.Health.IsUsable) return observed with { UnlockedIds = NormalizeIds(observed.UnlockedIds) };
        if (sameCharacter && retain && prior is not null && prior.Health.IsUsable && CanRetain(observed.Health))
            return prior with { Health = Stale(prior.Health, observed.Health.ReasonCode, observed.Health.Reason) };
        return observed with { UnlockedIds = [] };
    }

    public static CurrentItemPresenceState Resolve(CurrentItemPresenceState? prior, CurrentItemPresenceState observed, bool sameCharacter, bool retain)
    {
        if (observed.Health.IsUsable) return observed;
        if (sameCharacter && retain && prior is not null && prior.Health.IsUsable && CanRetain(observed.Health))
            return prior with { Health = Stale(prior.Health, observed.Health.ReasonCode, observed.Health.Reason) };
        return observed with { Items = [], DresserEntries = null };
    }

    public static CollectionSourceStatus Stale(CollectionSourceStatus source, ReasonCode reason, string detail, bool restored = false) =>
        source with
        {
            SourceStatus = SourceStatus.Stale,
            ReasonCode = reason,
            Reason = detail,
            ProvenanceType = restored ? ProvenanceType.Restored : source.ProvenanceType
        };

    public static ImmutableArray<uint> NormalizeIds(IEnumerable<uint> ids) => [.. ids.Where(id => id != 0).Distinct().Order()];

    public static CollectionState Restore(CollectionState state, ulong activeId, Guid session, DateTimeOffset now)
    {
        if (activeId == 0 || activeId != state.ContentId) throw new InvalidDataException("Stored character does not match the active scope.");
        CollectionSourceStatus RestoreHealth(CollectionSourceStatus health) => health.IsUsable
            ? Stale(health, ReasonCode.RestoredFromDisk, "Restored from disk.", true) : health;
        return state with
        {
            ProviderSessionId = session,
            CaptureType = CaptureType.Restored,
            CapturedAtUtc = now,
            Unlocks = state.Unlocks.ToImmutableDictionary(p => p.Key, p => p.Value with { Health = RestoreHealth(p.Value.Health) }),
            CurrentItemPresence = state.CurrentItemPresence.ToImmutableDictionary(p => p.Key, p => p.Value with { Health = RestoreHealth(p.Value.Health) }),
            ReportedEvidence = state.ReportedEvidence with { Items = [] },
        };
    }

    private static bool CanRetain(CollectionSourceStatus health) =>
        health.ReasonCode == ReasonCode.NotLoadedThisSession || health.ReasonCode == ReasonCode.WindowNotReady ||
        health.ReasonCode == ReasonCode.IncompleteContainerSet || health.ReasonCode == ReasonCode.IncompleteRetainerSet ||
        health.ReasonCode == ReasonCode.CaptureFailed;
}

