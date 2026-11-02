using System.Text.Json;

namespace CollectionShopper.Tests;

/// <summary>
/// Verifies tolerant parsing of nullable FFXIV Collect fields.
/// </summary>
public sealed class FfxivCollectModelTests
{
    /// <summary>
    /// Verifies null and Boolean tradeable values retain their meaning.
    /// </summary>
    [Theory]
    [InlineData("null", null)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void CollectableAcceptsNullableTradeableValues(string value, bool? expected)
    {
        // Arrange
        var json = $$"""
            {
              "id": 1,
              "name": "Test Unlock",
              "patch": "7.4",
              "item_id": 2,
              "tradeable": {{value}},
              "image": null,
              "icon": null,
              "sources": []
            }
            """;

        // Act
        var collectable = JsonSerializer.Deserialize<Collectable>(json, JsonOptions);

        // Assert
        Assert.NotNull(collectable);
        Assert.Equal(expected, collectable.Tradeable);
    }

    /// <summary>
    /// Verifies an omitted tradeable value remains unknown instead of false.
    /// </summary>
    [Fact]
    public void CollectableAcceptsMissingTradeableValue()
    {
        // Arrange
        const string json = """
            {
              "id": 1,
              "name": "Test Unlock",
              "patch": "7.4",
              "item_id": 2,
              "image": null,
              "icon": null,
              "sources": []
            }
            """;

        // Act
        var collectable = JsonSerializer.Deserialize<Collectable>(json, JsonOptions);

        // Assert
        Assert.NotNull(collectable);
        Assert.Null(collectable.Tradeable);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
