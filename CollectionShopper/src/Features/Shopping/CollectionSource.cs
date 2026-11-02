using System.Text.Json.Serialization;

namespace CollectionShopper;

internal sealed record CollectionSource(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text);

