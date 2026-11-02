using System.Text.Json;
using System.Text.Json.Serialization;

namespace CollectionShopper;

/// <summary>
/// Validates and parses the deterministic bundled outfit catalog payload.
/// </summary>
internal static class OutfitCatalogParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static IReadOnlyList<Outfit> Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var response = JsonSerializer.Deserialize<OutfitCatalogResponse>(stream, JsonOptions)
            ?? throw new InvalidDataException("The bundled outfit catalog is empty.");
        if (response.Results.Count == 0)
            throw new InvalidDataException("The bundled outfit catalog contains no outfits.");

        return response.Results;
    }

    private sealed record OutfitCatalogResponse(
        [property: JsonPropertyName("results")] IReadOnlyList<Outfit> Results);

}
