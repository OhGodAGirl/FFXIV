using System.Collections.Immutable;
using System.Globalization;
using BetterGlamorPlates.Shared.CollectionData;
using Kit = Exportaramus.ConsumerKit.V1_2;

namespace BetterGlamorPlates.Features.GlamourPlates;

/// <summary>Projects only complete, qualified physical presence into the local browsing model.</summary>
internal static class ExportaramusProjection
{
    internal static CollectionDataParseResult Project(Kit.CollectionDataClientView view, ulong contentId)
    {
        if (contentId == 0)
            return CollectionDataParseResult.Reject(CollectionDataFailure.NoCharacter);
        if (view.Availability != Kit.CollectionDataAvailability.Ready || view.Snapshot is not { } snapshot)
            return CollectionDataParseResult.Reject(CollectionDataFailure.ProviderUnavailable);
        if (snapshot.ContentId != contentId.ToString(CultureInfo.InvariantCulture))
            return CollectionDataParseResult.Reject(CollectionDataFailure.WrongCharacter);

        return CollectionDataParseResult.Accept(new CollectionDataSnapshot(
            snapshot.ProviderInternalName, snapshot.ProviderVersion, contentId, checked((ulong)snapshot.Revision),
            DateTimeOffset.Parse(snapshot.CapturedAtUtc, CultureInfo.InvariantCulture),
            Read(snapshot, "inventory"), Read(snapshot, "armoryChest"), Read(snapshot, "chocoboSaddlebag"),
            Read(snapshot, "premiumSaddlebag"), Read(snapshot, "retainers"),
            Read(snapshot, "armoire"), Read(snapshot, "glamourDresser")));
    }

    private static CollectionSourceSnapshot Read(Kit.CollectionDataSnapshot snapshot, string key)
    {
        if (!snapshot.SourceHealth.TryGetValue(key, out var health) ||
            !snapshot.SourceCapabilities.TryGetValue(key, out var capability) ||
            capability.CapabilityState != "Supported" || health.CapabilityState != "Supported" ||
            health.Completeness != "Complete" || health.SourceStatus is not ("Fresh" or "Stale") ||
            !snapshot.CurrentItemPresence.TryGetValue(key, out var presence))
            return new CollectionSourceSnapshot(CollectionSourceStatus.Unavailable, null, [], "Exportaramus has no complete usable observation.");

        var observed = DateTimeOffset.TryParse(health.ObservedAtUtc, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var timestamp) ? timestamp : (DateTimeOffset?)null;
        if (observed is null)
            return new CollectionSourceSnapshot(CollectionSourceStatus.NeverObserved, null, [], "No observation time is available.");
        return new CollectionSourceSnapshot(
            health.SourceStatus == "Fresh" ? CollectionSourceStatus.Fresh : CollectionSourceStatus.Stale,
            observed, presence.Items.Select(item => item.ItemId).Distinct().Order().ToImmutableArray(), health.Reason);
    }
}
