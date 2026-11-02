using System.Text.Json.Serialization;

namespace CollectionShopper;

internal sealed record Collectable(
    [property: JsonPropertyName("id")] uint Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("patch")] string Patch,
    [property: JsonPropertyName("item_id")] uint? ItemId,
    [property: JsonPropertyName("tradeable")] bool? Tradeable,
    [property: JsonPropertyName("image")] string? Image,
    [property: JsonPropertyName("icon")] string? Icon,
    [property: JsonPropertyName("sources")] IReadOnlyList<CollectionSource> Sources);

