using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_2;
using Exportaramus.Tests.Snapshots;

namespace Exportaramus.Tests.ContractsV12;

public sealed class Kit11ResidualRegressionTests
{
    [Fact]
    public void ExportedReportCodesMatchTheWireProtocol()
    {
        Assert.Equal(new[] { "Accepted", "InvalidRequest", "WrongCharacter", "RateLimited", "CapacityReached", "QueueFull", "Unavailable", "ProviderStopping" }, CollectionDataSemanticValidator.ReportCodes);
    }

    [Theory]
    [InlineData("1.0", CollectionDataValidationCode.UnsupportedSchemaVersion)]
    [InlineData(null, CollectionDataValidationCode.MissingSchemaVersion)]
    public void RichAdmissionPreservesSpecificVersionFailure(string? version, CollectionDataValidationCode expected)
    {
        var root = Rich();
        if (version is null) root.Remove("schemaVersion");
        else root["schemaVersion"] = version;
        Assert.Equal(expected, CollectionDataSemanticValidator.ValidateRich(root.ToJsonString()).Code);
    }

    [Fact]
    public void PhysicalDresserAcceptsEveryLegalEntryAndLastOutfitSlot()
    {
        var root = Rich();
        var entries = new JsonArray();
        for (var index = 0; index < 800; index++)
            entries.Add(new JsonObject
            {
                ["entryIndex"] = index,
                ["rawItemId"] = 1000100,
                ["baseItemId"] = 100,
                ["outfitId"] = 100,
                ["components"] = new JsonArray(new JsonObject { ["slotIndex"] = 10, ["itemId"] = 100 })
            });
        root["currentItemPresence"]!["glamourDresser"] = new JsonObject
        {
            ["items"] = new JsonArray(new JsonObject { ["itemId"] = 100 }),
            ["dresserEntries"] = entries
        };
        using var document = JsonDocument.Parse(root.ToJsonString());
        Assert.True(CollectionDataSemanticValidator.ValidateRich(document.RootElement));
        Assert.True(CollectionDataSemanticValidator.ValidateRich(root.ToJsonString()).Success);
    }

    [Theory]
    [InlineData("relics")]
    [InlineData("outfits")]
    public void MixedEvidenceCannotHideOneInvalidFact(string collection)
    {
        var root = Rich();
        var facts = root["collections"]![collection]!["items"]![0]!["ownershipEvidence"]!["facts"]!.AsArray();
        Assert.True(facts.Count > 1);
        if (collection == "relics")
        {
            var achievement = facts.Single(f => f!["sourceKey"]!.GetValue<string>() == "achievements")!;
            achievement["dataType"] = "Derived";
        }
        else
        {
            facts[0]!["sourceKey"] = "achievements";
            facts[0]!["dataType"] = "PermanentUnlock";
        }
        using var document = JsonDocument.Parse(root.ToJsonString());
        Assert.False(CollectionDataSemanticValidator.ValidateRich(document.RootElement));
    }

    private static JsonObject Rich() => JsonNode.Parse(V12FixtureAdapter.Rich(SnapshotFixture.Create(), "Test", SnapshotFixture.Now))!.AsObject();
}



