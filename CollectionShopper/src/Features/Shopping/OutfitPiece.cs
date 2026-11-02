using System.Text.Json.Serialization;

namespace CollectionShopper;

internal sealed record OutfitPiece(
    [property: JsonPropertyName("id")] uint Id,
    [property: JsonPropertyName("name")] string Name);

