using System.Collections.Immutable;

namespace Exportaramus.Features.Inventory;

public static class InventoryPolicy
{
    public const int MaximumRetainers = 10;
    public const int MaximumItemsPerLocation = 50000;
    public const int MaximumNameLength = 64;

    public static bool ValidName(string? name) => name is { Length: > 0 and <= MaximumNameLength } && !name.Any(char.IsControl);

    public static bool TryNormalize(IEnumerable<InventoryItemQuantity> items, out ImmutableArray<InventoryItemQuantity> normalized, bool supportsQuantity = true)
    {
        normalized = [];
        var quantities = new SortedDictionary<uint, InventoryItemQuantity>();
        var count = 0;
        foreach (var item in items)
        {
            if (++count > MaximumItemsPerLocation || item is null || item.ItemId is 0 or >= 1000000) return false;
            var total = (ulong)item.NormalQuantity + item.HighQualityQuantity;
            if (supportsQuantity ? total is 0 or > uint.MaxValue : total != 0) return false;
            quantities.TryGetValue(item.ItemId, out var prior);
            var normal = (ulong)(prior?.NormalQuantity ?? 0) + item.NormalQuantity;
            var high = (ulong)(prior?.HighQualityQuantity ?? 0) + item.HighQualityQuantity;
            if (normal + high > uint.MaxValue) return false;
            quantities[item.ItemId] = new(item.ItemId, (uint)normal, (uint)high);
        }
        normalized = quantities.Values.ToImmutableArray();
        return true;
    }

    public static InventoryLocationSnapshot Unknown(InventoryLocationKey key, string name) => new(key, name, InventoryObservationState.Unknown, null, null, []);

    public static InventorySnapshot Empty(ulong contentId, long revision) => new(contentId, revision, false,
        Enum.GetValues<InventoryLocationKind>().Where(kind => kind != InventoryLocationKind.Retainer)
            .Select(kind => Unknown(new(kind), kind.ToString())).ToImmutableArray());

    public static InventorySnapshot Restore(InventorySnapshot current, InventorySnapshot saved)
    {
        if (current.ContentId != saved.ContentId) return current;
        var locations = current.Locations.ToDictionary(location => location.Key);
        foreach (var location in saved.Locations)
        {
            if (location.Key.Kind == InventoryLocationKind.Retainer && current.RetainerRosterKnown && !locations.ContainsKey(location.Key)) continue;
            if (locations.TryGetValue(location.Key, out var live) && live.HasObservation) continue;
            var retained = location.HasObservation ? location with { State = InventoryObservationState.Retained, RestoredFromCache = true } : location;
            if (live is not null)
                retained = retained with { Name = live.Name, State = live.State == InventoryObservationState.ReadFailed ? live.State : retained.State, AttemptedAtUtc = live.AttemptedAtUtc ?? retained.AttemptedAtUtc };
            locations[location.Key] = retained;
        }
        return current with { Locations = Order(locations.Values) };
    }

    public static ImmutableArray<InventoryLocationSnapshot> Order(IEnumerable<InventoryLocationSnapshot> locations) =>
        locations.OrderBy(location => location.Key.Kind).ThenBy(location => location.Key.RetainerId).ToImmutableArray();
}

