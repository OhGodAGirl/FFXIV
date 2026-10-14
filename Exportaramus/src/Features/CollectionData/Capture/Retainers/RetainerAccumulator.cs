using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Features.CollectionData.Capture.Retainers;

public sealed class RetainerAccumulator
{
    private ulong character;
    private ImmutableHashSet<ulong> expected = [];
    private readonly Dictionary<ulong, ImmutableArray<PresenceItem>> observations = [];

    public void Reset() { character = 0; expected = []; observations.Clear(); }

    public CurrentItemPresenceState Observe(ulong contentId, ImmutableArray<ulong> expectedIds,
        ulong activeRetainerId, CurrentItemPresenceState? activeObservation, long generation, DateTimeOffset now)
    {
        if (contentId == 0 || expectedIds.IsDefault || expectedIds.Length > 10 || expectedIds.Any(id => id == 0)
            || expectedIds.Distinct().Count() != expectedIds.Length)
        {
            Reset();
            return CaptureReadPolicy.FailedPresence(true, generation, now, ReasonCode.IncompleteRetainerSet);
        }
        var next = expectedIds.ToImmutableHashSet();
        if (character != contentId || !expected.SetEquals(next))
        {
            observations.Clear(); expected = next; character = contentId;
        }
        if (expected.Contains(activeRetainerId) && activeObservation is { Health.IsUsable: true, Health.SourceStatus: SourceStatus.Fresh })
            observations[activeRetainerId] = activeObservation.Items;
        int observedCount = observations.Count;
        int expectedCount = expected.Count;
        if (observedCount != expectedCount)
            return CaptureReadPolicy.FailedPresence(true, generation, now, ReasonCode.IncompleteRetainerSet);
        return CaptureReadPolicy.Presence(observations.Values.SelectMany(v => v), true, true, generation, now);
    }
}
