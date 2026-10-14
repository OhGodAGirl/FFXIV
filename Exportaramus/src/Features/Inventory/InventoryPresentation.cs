using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.Main;

namespace Exportaramus.Features.Inventory;

public sealed record InventoryPresentation(string Summary, ImmutableArray<string> Locations, int? ConfirmedOutfits)
{
    public string RetainerCount { get; init; } = "Unknown";
    public static InventoryPresentation Empty { get; } = new("Waiting for character", [], null);
    public static InventoryPresentation Prepare(InventorySnapshot? snapshot, AcceptedSnapshotBundle? collections, DateTimeOffset now)
    {
        if (snapshot is null) return Empty;
        var retainers = snapshot.Locations.Where(location => location.Key.Kind == InventoryLocationKind.Retainer).ToArray();
        var observed = retainers.Count(location => location.HasObservation);
        var summary = snapshot.RetainerRosterKnown ? $"Retainers: {observed}/{retainers.Length} captured" : "Retainer list not yet observed";
        var rows = snapshot.Locations.Select(location =>
            $"{location.Name}: {(location.HasObservation ? location.Items.Length + " item types · " + MainPresenter.RelativeTime(location.ObservedAtUtc, now) : "Not yet captured")} · {location.State}").ToImmutableArray();
        int? outfits = null;
        if (collections?.Data.Catalog.Collections.TryGetValue("outfits", out var definition) == true)
        {
            var ids = snapshot.Locations.Where(location => location.HasObservation).SelectMany(location => location.Items).Select(item => item.ItemId).ToHashSet();
            foreach (var source in collections.Data.State.CurrentItemPresence.Values.Where(source => source.Health.IsUsable))
                foreach (var item in source.Items) ids.Add(item.ItemId);
            outfits = definition.Items.Count(item => !item.RequiredItemIds.IsDefaultOrEmpty && item.RequiredItemIds.All(ids.Contains));
        }
        return new(summary, rows, outfits) { RetainerCount = snapshot.RetainerRosterKnown ? $"{observed}/{retainers.Length}" : "Unknown" };
    }
}
