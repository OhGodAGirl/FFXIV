using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Features.CollectionData.Capture;

public sealed record RawCaptureBatch(ImmutableDictionary<string, PermanentUnlockState> Unlocks,
    ImmutableDictionary<string, CurrentItemPresenceState> CurrentItemPresence, ImmutableArray<QuestState> Quests);
