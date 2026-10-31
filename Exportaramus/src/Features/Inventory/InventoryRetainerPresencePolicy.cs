using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Features.Inventory;

/// <summary>Projects complete, same-character Retainer observations without refreshing their read time.</summary>
public static class InventoryRetainerPresencePolicy
{
    public static CurrentItemPresenceState Capture(InventorySnapshot? snapshot, ulong contentId,
        bool trustedCurrentBuild, long generation, DateTimeOffset captureStartedAtUtc)
    {
        CurrentItemPresenceState Failed(ReasonCode reason, bool validated = true) =>
            CaptureReadPolicy.FailedPresence(true, generation, captureStartedAtUtc, reason, validated);
        if (!trustedCurrentBuild) return Failed(ReasonCode.PatchValidationRequired, false);
        if (contentId == 0 || snapshot is null || snapshot.ContentId != contentId)
            return Failed(ReasonCode.IdentityUnavailable);
        if (!snapshot.RetainerRosterKnown || snapshot.Locations.IsDefault
            || snapshot.Locations.Length > InventoryPolicy.MaximumRetainers + Enum.GetValues<InventoryLocationKind>().Length - 1)
            return Failed(ReasonCode.IncompleteRetainerSet);
        if (captureStartedAtUtc == default) return Failed(ReasonCode.CaptureFailed);
        var ids = new HashSet<ulong>();
        var items = new List<PresenceItem>();
        var oldest = captureStartedAtUtc;
        var retained = false;
        var restored = false;
        foreach (var location in snapshot.Locations)
        {
            if (location is null) return Failed(ReasonCode.CaptureFailed);
            if (location.Key.Kind != InventoryLocationKind.Retainer) continue;
            if (!location.Key.IsValid || !ids.Add(location.Key.RetainerId)
                || ids.Count > InventoryPolicy.MaximumRetainers)
                return Failed(ReasonCode.IncompleteRetainerSet);
            if (location.State is not (InventoryObservationState.Observed or InventoryObservationState.Retained))
                return Failed(ReasonCode.IncompleteRetainerSet);
            if (location.ObservedAtUtc is not { } observed || observed == default || observed > captureStartedAtUtc
                || location.Items.IsDefault || location.Items.Length > InventoryPolicy.MaximumItemsPerLocation)
                return Failed(ReasonCode.CaptureFailed);
            oldest = observed < oldest ? observed : oldest;
            retained |= location.State == InventoryObservationState.Retained;
            restored |= location.RestoredFromCache;
            var itemIds = new HashSet<uint>();
            foreach (var item in location.Items)
            {
                if (item is null || item.ItemId is 0 or >= 1000000 || !itemIds.Add(item.ItemId))
                    return Failed(ReasonCode.CaptureFailed);
                var total = (ulong)item.NormalQuantity + item.HighQualityQuantity;
                if (total == 0) return Failed(ReasonCode.CaptureFailed);
                if (total > uint.MaxValue) return Failed(ReasonCode.QuantityOverflow);
                items.Add(new(item.ItemId, (uint)total));
            }
        }
        if (ids.Count == 0)
        {
            if (snapshot.RetainerRosterObservedAtUtc is not { } rosterTime || rosterTime == default || rosterTime > captureStartedAtUtc)
                return Failed(ReasonCode.IncompleteRetainerSet);
            oldest = rosterTime;
        }
        var result = CaptureReadPolicy.Presence(items, true, true, generation, oldest);
        return (retained || restored) && result.Health.IsUsable
            ? result with
            {
                Health = CollectionStatePolicy.Stale(result.Health, restored ? ReasonCode.RestoredFromDisk : ReasonCode.WindowNotReady,
                "Retainer observations are retained; the original observation time is preserved.", restored)
            }
            : result;
    }
}


