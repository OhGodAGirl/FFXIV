using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Tests.Snapshots;

namespace Exportaramus.Tests.Contracts;

public sealed class MutationSemanticEvidenceTests
{
    private static JsonObject Rich() => JsonNode.Parse(CollectionDataSerializer.Rich(SnapshotFixture.Create(), "Fixture", SnapshotFixture.Now))!.AsObject();
    private static JsonNode Item(JsonObject root, string key) => root["collections"]![key]!["items"]![0]!;
    private static JsonArray Facts(JsonObject root, string key) => Item(root, key)["ownershipEvidence"]!["facts"]!.AsArray();
    private static JsonObject Presence(string source, uint id, string state) => new() { ["dataType"] = "CurrentItemPresence", ["sourceKey"] = source, ["subjectId"] = id, ["state"] = state, ["sourceStatus"] = "Fresh" };
    private static void RemovePresence(JsonObject root, string source, uint id)
    {
        var items = root["currentItemPresence"]![source]!["items"]!.AsArray();
        for (var i = items.Count - 1; i >= 0; i--) if (items[i]!["itemId"]!.GetValue<uint>() == id) items.RemoveAt(i);
    }

    [Theory]
    [InlineData("description", "\"Text\"")]
    [InlineData("group", "\"Group\"")]
    [InlineData("category", "\"Category\"")]
    [InlineData("series", "\"Series\"")]
    [InlineData("level", "1")]
    [InlineData("location", "\"Location\"")]
    [InlineData("expansion", "\"Expansion\"")]
    [InlineData("festival", "false")]
    [InlineData("repeatable", "false")]
    [InlineData("points", "0")]
    [InlineData("rewards", "[]")]
    public void QuestDynamicExportRejectsEachIndependentlySchemaValidStaticField(string property, string value)
    {
        var root = Rich(); Item(root, "quests")[property] = JsonNode.Parse(value);
        using var document = JsonDocument.Parse(root.ToJsonString());
        Assert.True(ContractSchemaRegistry.Default.Validate(document.RootElement, "exportaramus-snapshot.schema.json"));
        Assert.Equal(CollectionDataValidationCode.SemanticValidationFailed, CollectionDataSemanticValidator.ValidateRich(root.ToJsonString()).Code);
    }

    [Fact]
    public void OutfitNeedsAllConstituentsButOnePositiveSourceSufficesForEachConstituent()
    {
        var root = Rich(); RemovePresence(root, "armoire", 2642); Facts(root, "outfits").Add(Presence("armoire", 2642, "Absent"));
        Assert.True(CollectionDataSemanticValidator.ValidateRich(root.ToJsonString()).Success);
        foreach (var source in CollectionDataSemanticValidator.PresenceKeys) RemovePresence(root, source, 3747);
        var facts = Facts(root, "outfits");
        for (var i = facts.Count - 1; i >= 0; i--) if (facts[i]!["subjectId"]!.GetValue<uint>() == 3747) facts.RemoveAt(i);
        foreach (var source in CollectionDataSemanticValidator.PresenceKeys) facts.Add(Presence(source, 3747, "Absent"));
        Item(root, "outfits")["owned"] = false;
        Assert.True(CollectionDataSemanticValidator.ValidateRich(root.ToJsonString()).Success);
        Item(root, "outfits")["owned"] = true;
        Assert.Equal(CollectionDataValidationCode.SemanticValidationFailed, CollectionDataSemanticValidator.ValidateRich(root.ToJsonString()).Code);
    }

    [Fact]
    public void RelicPhysicalPresenceRemainsPositiveAlongsideAnAbsentLinkedAchievement()
    {
        var root = Rich(); Item(root, "achievements")["owned"] = false; Facts(root, "achievements")[0]!["state"] = "Absent";
        Facts(root, "relics").Single(fact => fact!["sourceKey"]!.GetValue<string>() == "achievements")!["state"] = "Absent";
        Assert.Contains(Facts(root, "relics"), fact => fact!["dataType"]!.GetValue<string>() == "CurrentItemPresence" && fact["state"]!.GetValue<string>() == "Present");
        Assert.True(CollectionDataSemanticValidator.ValidateRich(root.ToJsonString()).Success);
        Item(root, "relics")["owned"] = false;
        Assert.Equal(CollectionDataValidationCode.SemanticValidationFailed, CollectionDataSemanticValidator.ValidateRich(root.ToJsonString()).Code);
    }

    [Theory]
    [InlineData(32, true)]
    [InlineData(33, false)]
    public void ReportLabelsUseTheExactUtf8BoundaryInsideActualCompactDocuments(int characters, bool expected)
    {
        var payload = ConsumerContractTests.Edit(ConsumerContractTests.Fixture("compact-valid-full.json"), root => root["reportedOwnership"]!["items"]![0]!["reporter"] = new string('é', characters));
        Assert.Equal(expected, CollectionDataSemanticValidator.ValidateSnapshot(payload, "12345678901234567").Success);
    }
}
