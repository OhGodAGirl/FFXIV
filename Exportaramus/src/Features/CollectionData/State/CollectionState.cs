using System.Collections.Immutable;
namespace Exportaramus.Features.CollectionData.State;

public sealed record CollectionState(
    ulong ContentId, Guid ProviderSessionId, long CaptureGeneration, CaptureType CaptureType,
    DateTimeOffset CapturedAtUtc,
    ImmutableDictionary<string, PermanentUnlockState> Unlocks,
    ImmutableDictionary<string, CurrentItemPresenceState> CurrentItemPresence,
    ReportedEvidenceState ReportedEvidence,
    ImmutableArray<QuestState> Quests);
