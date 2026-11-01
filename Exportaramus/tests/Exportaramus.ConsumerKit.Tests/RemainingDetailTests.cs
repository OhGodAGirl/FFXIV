using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_2;
namespace Exportaramus.ConsumerKit.Tests;

public sealed class RemainingDetailTests
{
    [Theory]
    [InlineData("group")]
    [InlineData("category")]
    [InlineData("series")]
    [InlineData("level")]
    [InlineData("location")]
    [InlineData("expansion")]
    [InlineData("festival")]
    [InlineData("repeatable")]
    [InlineData("rewards")]
    public void QuestsRejectOtherUnsupportedDetails(string field)
    {
        var root = JsonNode.Parse(ContractFixture.Read("expanded-valid-beasts-populated.json"))!;
        using (var baseline = JsonDocument.Parse(root.ToJsonString()))
            Assert.True(CollectionDataSemanticValidator.ValidateRich(baseline.RootElement));
        root["collections"]!["quests"]!["items"]![0]![field] = "Unsupported";
        using var changed = JsonDocument.Parse(root.ToJsonString());
        Assert.False(CollectionDataSemanticValidator.ValidateRich(changed.RootElement));
    }
    [Fact]
    public void DresserOutfitsAllowTheLastComponentSlot()
    {
        var root = JsonNode.Parse(ContractFixture.Read("expanded-valid-beasts-populated.json"))!;
        root["currentItemPresence"]!["glamourDresser"]!["dresserEntries"] = new JsonArray(new JsonObject
        {
            ["entryIndex"] = 0,
            ["rawItemId"] = 100,
            ["baseItemId"] = 100,
            ["outfitId"] = 100,
            ["components"] = new JsonArray(new JsonObject { ["slotIndex"] = 10, ["itemId"] = 100 }),
        });
        using var document = JsonDocument.Parse(root.ToJsonString());
        Assert.True(CollectionDataSemanticValidator.ValidateRich(document.RootElement));
    }
}
