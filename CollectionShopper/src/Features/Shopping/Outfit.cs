using System.Text.Json.Serialization;

namespace CollectionShopper;

internal sealed record Outfit(
    [property: JsonPropertyName("id")] uint Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("patch")] string Patch,
    [property: JsonPropertyName("gender")] string? Gender,
    [property: JsonPropertyName("icon")] string? Icon,
    [property: JsonPropertyName("items")] IReadOnlyList<OutfitPiece> Items,
    [property: JsonPropertyName("sources")] IReadOnlyList<CollectionSource> Sources);

