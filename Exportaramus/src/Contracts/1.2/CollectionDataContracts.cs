using System.Collections.Immutable;
using System.Text.Json;

namespace Exportaramus.ConsumerKit.V1_2;

public enum CollectionDataValidationCode
{
    None, NoPayload, PayloadTooLarge, InvalidJson, MissingSchemaVersion, MalformedSchemaVersion,
    UnsupportedSchemaVersion, ContractBindingFailed, SemanticValidationFailed, SchemaHashMismatch,
    InvalidContentId, WrongCharacter, WrongProviderSession, LowerRevision, EqualRevisionConflict,
    ProviderUnavailable, Disposed,
}

public sealed record CollectionDataValidationResult<T>(bool Success, T? Value, CollectionDataValidationCode Code, string Detail)
{
    public static CollectionDataValidationResult<T> Valid(T value) => new(true, value, CollectionDataValidationCode.None, "");
    public static CollectionDataValidationResult<T> Invalid(CollectionDataValidationCode code) => new(false, default, code, code.ToString());
}

public sealed record SourceCapability(string DataType, string CapabilityState, bool SupportsQuantity, string ReasonCode, string Reason);
public sealed record SourceHealth(string DataType, long CaptureGeneration, string SourceStatus, string Completeness,
    string ProvenanceType, string CapabilityState, string ReasonCode, string Reason, string? ObservedAtUtc);
public sealed record UnlockSet(ImmutableArray<uint> UnlockedIds);
public sealed record PresenceItem(uint ItemId, uint? Quantity);
public sealed record DresserEntryComponent(int SlotIndex, uint ItemId);
public sealed record DresserEntry(int EntryIndex, uint RawItemId, uint BaseItemId, uint? OutfitId, ImmutableArray<DresserEntryComponent> Components);
public sealed record PresenceSource(ImmutableArray<PresenceItem> Items)
{
    public ImmutableArray<DresserEntry>? DresserEntries { get; init; }
}
public sealed record ReportedEvidence(uint ItemId, string ReportedAtUtc, string Reporter);
public sealed record ReportedOwnership(ImmutableArray<ReportedEvidence> Items);
public sealed record CollectionDataSnapshot(string SchemaVersion, string ProviderInternalName, string ProviderVersion,
    string ProviderSessionId, long Revision, string ContentId, string CapturedAtUtc, string CaptureType,
    long CaptureGeneration, string Consistency, ImmutableDictionary<string, UnlockSet> Unlocks,
    ImmutableDictionary<string, PresenceSource> CurrentItemPresence, ReportedOwnership ReportedOwnership,
    ImmutableDictionary<string, SourceCapability> SourceCapabilities, ImmutableDictionary<string, SourceHealth> SourceHealth);
public sealed record GateNames(string GetCapabilities, string GetSnapshot, string SnapshotChanged, string SnapshotWithdrawn, string ReportOwnershipEvidence);
public sealed record SchemaIdentity(string Id, string Sha256);
public sealed record ReportRate(int AcceptedPerMinute, int Burst);
public sealed record OperationalLimits(long SnapshotUtf8Bytes, int CapabilitiesUtf8Bytes, int EventUtf8Bytes, int IdsPerCollection,
    int ReportedEvidenceRecordsPerCharacter, int ReporterUtf8Bytes, int PendingReportsTotal, int PendingReportsPerReporter,
    long QuantityMaximum, ReportRate GlobalReportRate, ReportRate ReporterReportRate);
public sealed record ReportContract(bool Available, ImmutableArray<string> ResultCodes);
public sealed record CollectionDataCapabilities(string SchemaVersion, string ProviderInternalName, string ProviderVersion,
    string ProviderSessionId, string ClientLanguage, string GameDataVersion, GateNames Gates,
    ImmutableArray<string> UnlockKeys, ImmutableArray<string> CurrentPresenceSources,
    ImmutableDictionary<string, SourceCapability> SourceCapabilities, SchemaIdentity CollectionDataSchema,
    OperationalLimits Limits, ReportContract ReportedOwnershipEvidence);
public sealed record CollectionDataChangedNotice(string EventType, string SchemaVersion, string ProviderSessionId,
    long Revision, string ContentId, string CapturedAtUtc, ImmutableArray<string> ChangedDataTypes, ImmutableArray<string> ChangedSources);
public sealed record CollectionDataWithdrawnNotice(string EventType, string SchemaVersion, string ProviderSessionId,
    string ContentId, string WithdrawnAtUtc, string ReasonCode, string? Explanation);
public enum CollectionDataAvailability { Unavailable, Refreshing, Ready, Rejected, Disposed }
public sealed record CollectionDataClientView(CollectionDataAvailability Availability, CollectionDataSnapshot? Snapshot,
    string? ProviderSessionId, string? ContentId, long Revision, CollectionDataValidationCode Failure)
{
    public static CollectionDataClientView Empty(CollectionDataAvailability availability, CollectionDataValidationCode failure) => new(availability, null, null, null, 0, failure);
}

public sealed record RichSnapshotDocument(JsonElement Root);
public sealed record ValidatedJsonDocument(JsonElement Root);
