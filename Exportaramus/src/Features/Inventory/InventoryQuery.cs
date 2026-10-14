using System.Globalization;
using System.Text.Json;

namespace Exportaramus.Features.Inventory;

public static class InventoryQuery
{
    public const string Capabilities = "{\"schemaVersion\":\"1.0\",\"scope\":\"CurrentCharacter\",\"quantityQuality\":[\"Normal\",\"HighQuality\"],\"retainedIsCurrent\":false}";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static string Snapshot(InventorySnapshot? snapshot) => JsonSerializer.Serialize(new
    {
        schemaVersion = "1.0",
        available = snapshot is not null,
        contentId = snapshot?.ContentId.ToString(CultureInfo.InvariantCulture),
        revision = snapshot?.Revision ?? 0,
        retainerRosterKnown = snapshot?.RetainerRosterKnown ?? false,
        locations = snapshot?.Locations.Select(location => new
        {
            kind = location.Key.Kind.ToString(),
            retainerId = location.Key.RetainerId.ToString(CultureInfo.InvariantCulture),
            location.Name,
            state = location.State.ToString(),
            location.ObservedAtUtc,
            location.AttemptedAtUtc,
            supportsQuantity = Quantified(location),
            items = location.Items.Select(item => new
            {
                item.ItemId,
                normalQuantity = Quantified(location) ? (uint?)item.NormalQuantity : null,
                highQualityQuantity = Quantified(location) ? (uint?)item.HighQualityQuantity : null
            })
        })
    }, Json);
    public static string Item(InventorySnapshot? snapshot, uint itemId)
    {
        if (itemId == 0) return "{\"schemaVersion\":\"1.0\",\"error\":\"InvalidItemId\"}";
        var locations = snapshot?.Locations.Where(location => location.HasObservation).ToArray() ?? [];
        var matches = locations.SelectMany(location => location.Items.Where(item => item.ItemId == itemId).Select(item => new
        {
            kind = location.Key.Kind.ToString(),
            retainerId = location.Key.RetainerId.ToString(CultureInfo.InvariantCulture),
            location.Name,
            state = location.State.ToString(),
            location.ObservedAtUtc,
            normalQuantity = Quantified(location) ? (uint?)item.NormalQuantity : null,
            highQualityQuantity = Quantified(location) ? (uint?)item.HighQualityQuantity : null
        })).ToArray();
        return JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0",
            available = snapshot is not null,
            itemId,
            contentId = snapshot?.ContentId.ToString(CultureInfo.InvariantCulture),
            revision = snapshot?.Revision ?? 0,
            normalQuantity = matches.Aggregate(0UL, (sum, item) => checked(sum + (item.normalQuantity ?? 0))),
            highQualityQuantity = matches.Aggregate(0UL, (sum, item) => checked(sum + (item.highQualityQuantity ?? 0))),
            quantityMeaning = "LastObservedQuantifiedLocationsOnly",
            hasUnknownLocations = snapshot is null || !snapshot.RetainerRosterKnown || snapshot.Locations.Any(location => !location.HasObservation),
            hasRetainedLocations = locations.Any(location => location.State != InventoryObservationState.Observed),
            locations = matches
        }, Json);
    }
    private static bool Quantified(InventoryLocationSnapshot location) => location.Key.Kind is not (InventoryLocationKind.Armoire or InventoryLocationKind.GlamourDresser);
}
