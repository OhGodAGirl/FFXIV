using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_1;
using Exportaramus.Tests.Snapshots;
namespace Exportaramus.Tests.ContractsV11;

public sealed class DresserSemanticBoundaryTests
{
    private static JsonObject Rich()
    {
        var root = JsonNode.Parse(V11FixtureAdapter.Rich(SnapshotFixture.Create(), "Test", SnapshotFixture.Now))!.AsObject();
        root["currentItemPresence"]!["glamourDresser"] = JsonNode.Parse("""{"items":[{"itemId":100}],"dresserEntries":[{"entryIndex":0,"rawItemId":100,"baseItemId":100,"outfitId":null,"components":[{"slotIndex":0,"itemId":100}]}]}""");
        return root;
    }
    [Fact]
    public void StandaloneSemanticEntryPointRejectsMalformedPhysicalEvidenceEvenBeforeSchemaBinding()
    {
        var valid = Rich();
        using (var document = JsonDocument.Parse(valid.ToJsonString())) Assert.True(CollectionDataSemanticValidator.ValidateRich(document.RootElement));
        foreach (var edit in new Action<JsonObject>[]
        {
            n => n["currentItemPresence"]!["glamourDresser"]!["dresserEntries"] = new JsonObject(),
            n => n["currentItemPresence"]!["glamourDresser"]!["dresserEntries"] = new JsonArray(Enumerable.Range(0, 801).Select(_ => (JsonNode?)new JsonObject()).ToArray()),
            n => Entry(n)["entryIndex"] = 800,
            n => Entry(n)["rawItemId"] = 1000000,
            n => { Entry(n)["rawItemId"] = 1000000; Entry(n)["baseItemId"] = 0; },
            n => Entry(n)["components"] = new JsonArray(),
            n => Entry(n)["components"]![0]!["slotIndex"] = 1,
            n => Entry(n)["components"]![0]!["itemId"] = 101,
            n => Entry(n)["outfitId"] = 101,
            n => { Entry(n)["outfitId"] = 100; Entry(n)["components"]![0]!["slotIndex"] = -1; },
            n => { Entry(n)["outfitId"] = 100; Entry(n)["components"]![0]!["itemId"] = 0; },
        })
        {
            var root = valid.DeepClone().AsObject(); edit(root);
            using var document = JsonDocument.Parse(root.ToJsonString());
            Assert.False(CollectionDataSemanticValidator.ValidateRich(document.RootElement));
        }
    }
    private static JsonNode Entry(JsonObject root) => root["currentItemPresence"]!["glamourDresser"]!["dresserEntries"]![0]!;
}
