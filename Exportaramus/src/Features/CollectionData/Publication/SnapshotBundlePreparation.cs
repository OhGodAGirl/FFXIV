using System.Collections.Immutable;
using System.Text;
namespace Exportaramus.Features.CollectionData.Publication;

public static class SnapshotBundlePreparation
{
    public static AcceptedSnapshotBundle Prepare(AcceptedSnapshotBundle bundle, AcceptedSnapshotBundle? previous, bool reportsAvailable)
    {
        var counts = bundle.Data.Collections.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Items.Count(item => item.Owned));
        var deltas = previous is null || previous.Data.State.ContentId != bundle.Data.State.ContentId
            ? ImmutableDictionary<string, int>.Empty
            : counts.Where(pair => previous.CollectionOwnedCounts.ContainsKey(pair.Key))
                .ToImmutableDictionary(pair => pair.Key, pair => pair.Value - previous.CollectionOwnedCounts[pair.Key]);
        var capabilities = CapabilityDocumentWriter.Build(bundle.Data, reportsAvailable);
        return bundle with
        {
            DataFingerprints = CollectionFingerprint.Sources(bundle.Data, false),
            CollectionOwnedCounts = counts,
            CollectionDeltas = deltas,
            ConfirmedPartialCounts = ConfirmedPartialCounts(bundle),
            CapabilitiesJson = capabilities,
            CompactJsonV11 = CollectionDataSerializer.CompactV11(bundle.Data, bundle.Revision),
            CompactJsonV12 = CollectionDataSerializer.CompactV12(bundle.Data, bundle.Revision),
            CapabilitiesJsonV12 = CapabilityDocumentWriter.BuildV12(bundle.Data, reportsAvailable),
            UnavailableReportsCapabilitiesJsonV12 = CapabilityDocumentWriter.BuildV12(bundle.Data, false),
            CapabilitiesJsonV11 = CapabilityDocumentWriter.BuildV11(bundle.Data, reportsAvailable),
            UnavailableReportsCapabilitiesJsonV11 = CapabilityDocumentWriter.BuildV11(bundle.Data, false),
            CapabilitiesBytes = Encoding.UTF8.GetByteCount(capabilities),
            UnavailableReportsCapabilitiesJson = reportsAvailable ? CapabilityDocumentWriter.Build(bundle.Data, false) : capabilities,
            CompactBytes = Encoding.UTF8.GetByteCount(bundle.CompactJson),
            RichBytes = Encoding.UTF8.GetByteCount(bundle.RichJson ?? ""),
            ExportBytes = Encoding.UTF8.GetByteCount(bundle.Export.Json ?? ""),
        };
    }

    private static ImmutableDictionary<string, int> ConfirmedPartialCounts(AcceptedSnapshotBundle bundle)
    {
        if (bundle.Data.Collections.ContainsKey("outfits")) return ImmutableDictionary<string, int>.Empty;
        var available = bundle.Data.State.CurrentItemPresence.Values.Where(source => source.Health.IsUsable).ToArray();
        if (available.Length == 0 || !bundle.Data.Catalog.Collections.TryGetValue("outfits", out var outfits) || outfits.Items.IsDefaultOrEmpty)
            return ImmutableDictionary<string, int>.Empty;
        var items = available.SelectMany(source => source.Items).Select(item => item.ItemId).ToHashSet();
        var count = outfits.Items.Count(row => !row.RequiredItemIds.IsDefaultOrEmpty && row.RequiredItemIds.All(items.Contains));
        return ImmutableDictionary<string, int>.Empty.Add("outfits", count);
    }
}
