using System.Text;
using System.Text.Json;

namespace CollectionShopper.Tests;

/// <summary>
/// Verifies the packaged outfit-definition snapshot remains usable.
/// </summary>
public sealed class OutfitCatalogTests
{
    /// <summary>
    /// Verifies the release assembly contains a populated, internally consistent catalog.
    /// </summary>
    [Fact]
    public void LoadBundledReturnsPopulatedDistinctOutfits()
    {
        // Arrange and Act
        var outfits = OutfitCatalog.LoadBundled();

        // Assert
        Assert.True(outfits.Count > 1_000);
        Assert.Equal(outfits.Count, outfits.Select(outfit => outfit.Id).Distinct().Count());
        Assert.All(outfits, outfit =>
        {
            Assert.NotEqual(0U, outfit.Id);
            Assert.False(string.IsNullOrWhiteSpace(outfit.Name));
            Assert.NotEmpty(outfit.Items);
        });
    }

    /// <summary>
    /// Verifies an empty snapshot fails explicitly instead of silently producing no outfits.
    /// </summary>
    [Fact]
    public void ParseRejectsEmptyCatalog()
    {
        // Arrange
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"results\":[]}"));

        // Act
        var action = () => OutfitCatalogParser.Parse(stream);

        // Assert
        var exception = Assert.Throws<InvalidDataException>(action);
        Assert.Equal("The bundled outfit catalog contains no outfits.", exception.Message);
    }

    /// <summary>
    /// Verifies missing and JSON-null inputs fail with distinct boundary errors.
    /// </summary>
    [Fact]
    public void ParseRejectsMissingAndNullPayloads()
    {
        Assert.Equal("stream", Assert.Throws<ArgumentNullException>(
            () => OutfitCatalogParser.Parse(null!)).ParamName);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("null"));
        var exception = Assert.Throws<InvalidDataException>(
            () => OutfitCatalogParser.Parse(stream));
        Assert.Equal("The bundled outfit catalog is empty.", exception.Message);
    }

    /// <summary>
    /// Verifies parser output preserves the complete ordered payload.
    /// </summary>
    [Fact]
    public void ParsePreservesValidOutfitPayload()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""
            {
              "results": [
                {
                  "id": 7,
                  "name": "Fixture Set",
                  "patch": "7.0",
                  "gender": null,
                  "icon": "fixture.png",
                  "items": [{ "id": 42, "name": "Fixture Piece" }],
                  "sources": [{ "type": "Fixture", "text": "Fixture Source" }]
                }
              ]
            }
            """));

        var outfit = Assert.Single(OutfitCatalogParser.Parse(stream));

        Assert.Equal(7U, outfit.Id);
        Assert.Equal("Fixture Set", outfit.Name);
        Assert.Equal("7.0", outfit.Patch);
        Assert.Null(outfit.Gender);
        Assert.Equal("fixture.png", outfit.Icon);
        Assert.Equal(new OutfitPiece(42, "Fixture Piece"), Assert.Single(outfit.Items));
        Assert.Equal(new CollectionSource("Fixture", "Fixture Source"), Assert.Single(outfit.Sources));
    }

    /// <summary>
    /// Verifies generated valid catalogs preserve count, order, identity, and nested pieces.
    /// </summary>
    [Fact]
    public void GeneratedCatalogPayloadsPreserveOrderAndContent()
    {
        for (var iteration = 0; iteration < 128; iteration++)
        {
            var expectedIds = Enumerable.Range(1, (iteration % 16) + 1)
                .Select(index => (uint)((iteration * 100) + index))
                .ToArray();
            var payload = new
            {
                results = expectedIds.Select(id => new
                {
                    id,
                    name = $"Fixture {id}",
                    patch = "7.0",
                    gender = (string?)null,
                    icon = (string?)null,
                    items = new[] { new { id = id + 10_000, name = $"Piece {id}" } },
                    sources = Array.Empty<object>(),
                }),
            };
            using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(payload));

            var parsed = OutfitCatalogParser.Parse(stream);

            Assert.Equal(expectedIds, parsed.Select(outfit => outfit.Id));
            Assert.All(parsed, outfit =>
            {
                Assert.Equal($"Fixture {outfit.Id}", outfit.Name);
                Assert.Equal(outfit.Id + 10_000, Assert.Single(outfit.Items).Id);
            });
        }
    }
}
