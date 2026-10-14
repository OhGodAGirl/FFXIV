using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Features.CollectionData.Capture;

public static class TargetedCaptureMerge
{
    public static CollectionState Apply(CollectionState prior, RawCaptureBatch raw, long generation, DateTimeOffset now)
    {
        var unlocks = prior.Unlocks.ToBuilder();
        foreach (var pair in raw.Unlocks)
            unlocks[pair.Key] = CollectionStatePolicy.Resolve(prior.Unlocks.GetValueOrDefault(pair.Key), pair.Value, true, true);
        var presence = prior.CurrentItemPresence.ToBuilder();
        foreach (var pair in raw.CurrentItemPresence)
            presence[pair.Key] = CollectionStatePolicy.Resolve(prior.CurrentItemPresence.GetValueOrDefault(pair.Key), pair.Value, true, true);
        return prior with
        {
            CaptureGeneration = generation,
            CapturedAtUtc = now,
            CaptureType = CaptureType.Lightweight,
            Unlocks = unlocks.ToImmutable(),
            CurrentItemPresence = presence.ToImmutable(),
            Quests = !raw.Unlocks.TryGetValue("quests", out var questRead) ? prior.Quests
                : questRead.Health.IsUsable ? raw.Quests : unlocks["quests"].Health.IsUsable ? prior.Quests : [],
        };
    }
}
