using System.Collections.Immutable;
using System.Globalization;

namespace CollectionShopper;

internal sealed record PurchaseTrackingState(
    ulong ContentId,
    bool IsValid,
    ImmutableHashSet<uint> PendingItemIds,
    ImmutableDictionary<uint, DateTimeOffset> AcknowledgedAcquisitions,
    ImmutableHashSet<uint> LegacyPurchaseArchive);

internal static class PurchaseTrackingPolicy
{
    internal static PurchaseTrackingState Prepare(IReadOnlyList<PendingPurchase>? records,
        IReadOnlySet<uint> completedItemIds, ulong contentId, DateTimeOffset now)
    {
        if (!PendingPurchaseGuard.IsValid(records, now))
            return new(contentId, false, [], ImmutableDictionary<uint, DateTimeOffset>.Empty, []);

        var currentRecords = records!.Where(purchase => purchase.ContentId == contentId).ToArray();
        return new(contentId, true,
            currentRecords.Where(purchase => purchase.AcknowledgedAtUtc is null)
                .Select(purchase => purchase.ItemId).ToImmutableHashSet(),
            currentRecords.Where(purchase => purchase.AcknowledgedAtUtc is not null)
                .ToImmutableDictionary(purchase => purchase.ItemId, purchase => purchase.AcknowledgedAtUtc!.Value),
            completedItemIds.Except(currentRecords.Select(purchase => purchase.ItemId)).ToImmutableHashSet());
    }

    internal static bool Reconcile(PurchaseTrackingState tracking, List<PendingPurchase> records,
        ulong contentId, CollectionDataOwnershipState ownership)
    {
        if (!tracking.IsValid) return false;
        var changed = false;
        for (var index = 0; index < records.Count; index++)
        {
            var pending = records[index];
            if (pending.ContentId != contentId || pending.AcknowledgedAtUtc is not null) continue;
            var observed = PendingPurchaseGuard.ObservedAfter(pending, ownership);
            if (observed is null) continue;
            records[index] = pending with { AcknowledgedAtUtc = observed.Value };
            changed = true;
        }
        return changed;
    }

    internal static CollectionDataOwnershipState ApplyOwnership(PurchaseTrackingState tracking,
        ulong contentId, CollectionDataOwnershipState ownership)
    {
        if (!tracking.IsValid)
            return CollectionDataOwnershipPolicy.NotChecked with { StatusDetail = "Saved purchase tracking is invalid. Purchases are blocked; existing records have been preserved." };
        return ownership.Snapshot is { } snapshot
            && snapshot.ContentId != contentId.ToString(CultureInfo.InvariantCulture)
                ? CollectionDataOwnershipPolicy.NotChecked
                : ownership with { AcknowledgedAcquisitions = tracking.AcknowledgedAcquisitions, LegacyPurchaseArchive = tracking.LegacyPurchaseArchive };
    }

    internal static ItemOwnership Readiness(PurchaseTrackingState tracking, IReadOnlyList<PendingPurchase> records,
        ShoppingItem item, ulong contentId, ulong runContentId, CollectionDataOwnershipState ownership)
    {
        if (!tracking.IsValid) return new(false, false, false, "Saved purchase tracking is invalid; purchases are blocked.");
        if (records.Count >= PendingPurchaseGuard.MaximumRecords
            && !records.Any(purchase => purchase.ContentId == contentId && purchase.ItemId == item.ItemId))
            return new(false, false, false, "Purchase tracking has reached its safety limit.");
        return PurchaseReadinessPolicy.Evaluate(item, contentId, runContentId,
            ApplyOwnership(tracking, contentId, ownership), tracking.PendingItemIds);
    }

    internal static bool RecordConfirmed(List<PendingPurchase> records, ISet<uint> completedItemIds,
        uint itemId, ulong purchaseContentId, DateTimeOffset now)
    {
        if (itemId == 0) return false;
        completedItemIds.Add(itemId);
        records.RemoveAll(purchase => purchase.ContentId == purchaseContentId && purchase.ItemId == itemId);
        records.Add(new(purchaseContentId, itemId, now));
        return true;
    }
}
