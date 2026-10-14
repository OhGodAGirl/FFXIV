using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Features.CollectionData.Capture;

public static class TargetedCaptureChangePolicy
{
    public static bool HasChanges(CollectionState prior, CollectionState next, RawCaptureBatch raw)
    {
        foreach (var key in raw.Unlocks.Keys)
        {
            var value = next.Unlocks[key];
            if (!prior.Unlocks.TryGetValue(key, out var old) || !SameHealth(old.Health, value.Health) || !old.UnlockedIds.SequenceEqual(value.UnlockedIds)) return true;
        }
        foreach (var key in raw.CurrentItemPresence.Keys)
        {
            var value = next.CurrentItemPresence[key];
            if (!prior.CurrentItemPresence.TryGetValue(key, out var old) || old.SupportsQuantity != value.SupportsQuantity ||
                !SameHealth(old.Health, value.Health) || !old.Items.SequenceEqual(value.Items) || !SameEntries(old.DresserEntries, value.DresserEntries)) return true;
        }
        return raw.Unlocks.ContainsKey("quests") && !prior.Quests.SequenceEqual(next.Quests);
    }

    private static bool SameHealth(CollectionSourceStatus left, CollectionSourceStatus right) =>
        left.DataType == right.DataType && left.SourceStatus == right.SourceStatus && left.Completeness == right.Completeness &&
        left.ProvenanceType == right.ProvenanceType && left.CapabilityState == right.CapabilityState && left.ReasonCode == right.ReasonCode && left.Reason == right.Reason;

    private static bool SameEntries(ImmutableArray<DresserEntry>? left, ImmutableArray<DresserEntry>? right)
    {
        if (left is null || right is null) return left is null && right is null;
        if (left.Value.Length != right.Value.Length) return false;
        for (var i = 0; i < left.Value.Length; i++)
        {
            var a = left.Value[i]; var b = right.Value[i];
            if (a.EntryIndex != b.EntryIndex || a.RawItemId != b.RawItemId || a.BaseItemId != b.BaseItemId || a.OutfitId != b.OutfitId ||
                !a.Components.SequenceEqual(b.Components)) return false;
        }
        return true;
    }
}
