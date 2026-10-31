using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Features.CollectionData.Capture;

public static class CaptureReadPolicy
{
    public static CollectionSourceStatus Health(DataType type, long generation, DateTimeOffset now, ReasonCode reason,
        bool validated = true) => new(type, reason == ReasonCode.None ? SourceStatus.Fresh : SourceStatus.Unavailable,
            reason == ReasonCode.None ? Completeness.Complete : Completeness.Unknown,
            reason == ReasonCode.None ? ProvenanceType.Observed : ProvenanceType.None,
            validated ? CapabilityState.Supported : CapabilityState.UnavailablePendingValidation,
            reason, reason == ReasonCode.None ? "" : reason == ReasonCode.PatchValidationRequired
                ? "This source requires compatibility review for this game build." : "The source could not be completely read. Try again when it is ready.",
            reason == ReasonCode.None ? now : null, generation);

    public static CurrentItemPresenceState Presence(IEnumerable<PresenceItem> items, bool quantity, bool complete,
        long generation, DateTimeOffset now)
    {
        if (!complete) return FailedPresence(quantity, generation, now, ReasonCode.IncompleteContainerSet);
        var result = new SortedDictionary<uint, ulong>();
        foreach (var item in items)
        {
            if (item.ItemId == 0) continue;
            if (quantity && item.Quantity is null or 0)
                return FailedPresence(quantity, generation, now, ReasonCode.CaptureFailed);
            var sum = result.GetValueOrDefault(item.ItemId) + (quantity ? item.Quantity!.Value : 1ul);
            if (quantity && sum > uint.MaxValue) return FailedPresence(quantity, generation, now, ReasonCode.QuantityOverflow);
            result[item.ItemId] = quantity ? sum : 1;
            int distinctCount = result.Count;
            if (distinctCount > 50000) return FailedPresence(quantity, generation, now, ReasonCode.OutputSizeLimitExceeded);
        }
        return new CurrentItemPresenceState(result.Select(p => new PresenceItem(p.Key, quantity ? (uint)p.Value : null)).ToImmutableArray(),
            quantity, Health(DataType.CurrentItemPresence, generation, now, ReasonCode.None));
    }

    public static CurrentItemPresenceState FailedPresence(bool quantity, long generation, DateTimeOffset now,
        ReasonCode reason, bool validated = true) => new([], quantity, Health(DataType.CurrentItemPresence, generation, now, reason, validated));
}
