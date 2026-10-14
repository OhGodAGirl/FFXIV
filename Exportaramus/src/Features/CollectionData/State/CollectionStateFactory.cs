using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Contracts;
namespace Exportaramus.Features.CollectionData.State;

public static class CollectionStateFactory
{
    public static CollectionState Empty(ulong contentId, Guid session, DateTimeOffset now, long generation = 0)
    {
        CollectionSourceStatus Unknown(DataType type) => new(type, SourceStatus.NeverObserved, Completeness.Unknown,
            ProvenanceType.None, CapabilityState.UnavailablePendingValidation, ReasonCode.PatchValidationRequired,
            "This source requires supervised validation.", null, generation);
        return new(contentId, session, generation, CaptureType.Full, now,
            CollectionKeys.Unlocks.ToImmutableDictionary(key => key, _ => new PermanentUnlockState([], Unknown(DataType.PermanentUnlock))),
            CollectionKeys.Presence.ToImmutableDictionary(key => key, key => new CurrentItemPresenceState([], CollectionKeys.SupportsQuantity(key), Unknown(DataType.CurrentItemPresence))),
            new([], new(DataType.ReportedOwnership, SourceStatus.Fresh, Completeness.Complete, ProvenanceType.Reported,
                CapabilityState.Supported, ReasonCode.None, "", now, generation)), []);
    }
}
