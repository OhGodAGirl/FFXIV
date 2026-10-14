namespace Exportaramus.Features.CollectionData.State;

public enum SourceStatus { Fresh, Stale, Unavailable, NeverObserved }
public enum Completeness { Complete, Partial, Unknown }
public enum ProvenanceType { None, Observed, Restored, Reported, Derived }
public enum CapabilityState { Supported, Degraded, UnavailablePendingValidation }
public enum DataType { PermanentUnlock, CurrentItemPresence, Derived, ReportedOwnership }
public enum CaptureType { Full, Lightweight, Restored, Reported }
public enum ReasonCode
{
    None, NotLoadedThisSession, WindowNotReady, IncompleteContainerSet, IncompleteRetainerSet,
    MappingValidationFailed, PatchValidationRequired, RestoredFromDisk, CharacterUnavailable,
    IdentityUnavailable, QuantityOverflow, CaptureFailed, GameDataVersionUnsupported,
    OutputSizeLimitExceeded, SerializerValidationFailed,
}

public sealed record CollectionSourceStatus(
    DataType DataType, SourceStatus SourceStatus, Completeness Completeness,
    ProvenanceType ProvenanceType, CapabilityState CapabilityState, ReasonCode ReasonCode,
    string Reason, DateTimeOffset? ObservedAtUtc, long CaptureGeneration)
{
    public bool IsUsable => (SourceStatus is SourceStatus.Fresh or SourceStatus.Stale)
        && Completeness == Completeness.Complete && CapabilityState != CapabilityState.UnavailablePendingValidation;
}
