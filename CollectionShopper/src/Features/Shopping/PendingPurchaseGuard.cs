using System.Globalization;

namespace CollectionShopper;

internal sealed record PendingPurchase(ulong ContentId, uint ItemId, DateTimeOffset ConfirmedAtUtc,
    DateTimeOffset? AcknowledgedAtUtc = null);

internal static class PendingPurchaseGuard
{
    internal const int MaximumRecords = 4096;

    internal static bool IsValid(IReadOnlyList<PendingPurchase>? records, DateTimeOffset now)
    {
        if (records is null || records.Count > MaximumRecords) return false;
        var keys = new HashSet<(ulong, uint)>();
        foreach (var record in records)
        {
            if (record is null || record.ContentId == 0 || record.ItemId == 0
                || record.ConfirmedAtUtc > now
                || record.AcknowledgedAtUtc is { } acknowledged
                    && (acknowledged < record.ConfirmedAtUtc || acknowledged > now)
                || !keys.Add((record.ContentId, record.ItemId))) return false;
        }
        return true;
    }

    internal static bool CanRetire(PendingPurchase guard, CollectionDataOwnershipState ownership)
        => ObservedAfter(guard, ownership) is not null;

    internal static DateTimeOffset? ObservedAfter(PendingPurchase guard, CollectionDataOwnershipState ownership)
    {
        var snapshot = ownership.Snapshot;
        if (guard.ContentId == 0 || guard.ItemId == 0 || snapshot is null
            || snapshot.ContentId != guard.ContentId.ToString(CultureInfo.InvariantCulture))
            return null;

        // Reports and newer snapshot revisions do not prove a new physical observation.
        foreach (var source in CollectionDataOwnershipPolicy.PhysicalSources)
        {
            if (!snapshot.SourceHealth.TryGetValue(source, out var health)
                || health.SourceStatus != "Fresh" || health.Completeness != "Complete"
                || health.CapabilityState != "Supported")
                continue;

            var parsed = DateTimeOffset.TryParse(health.ObservedAtUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var observedAt);
            if (!parsed || observedAt <= guard.ConfirmedAtUtc)
                continue;

            var hasPresence = snapshot.CurrentItemPresence.TryGetValue(source, out var presence);
            if (!hasPresence)
                continue;
            if (presence!.Items.Any(item => item.ItemId == guard.ItemId))
                return observedAt;
        }

        return null;
    }
}
