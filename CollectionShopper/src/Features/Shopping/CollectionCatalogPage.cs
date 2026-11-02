using System.Text.Json.Serialization;

namespace CollectionShopper;

internal sealed record CollectionCatalogPage(
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("results")] IReadOnlyList<Collectable>? Results);

