using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_2;

namespace Exportaramus.ConsumerKit.Tests;

public sealed class ContractAdditionalBoundaryTests
{
    [Fact]
    public void SuccessfulValidationHasNoErrorDetail()
    {
        var result = CollectionDataValidationResult<string>.Valid("payload");
        Assert.True(result.Success);
        Assert.Equal("payload", result.Value);
        Assert.Equal(CollectionDataValidationCode.None, result.Code);
        Assert.Empty(result.Detail);
    }

    [Fact]
    public void ReportCodesMatchTheAdvertisedContract()
    {
        var root = JsonNode.Parse(ContractFixture.Read("capabilities-valid.json"))!;
        var expected = root["reportedOwnershipEvidence"]!["resultCodes"]!.AsArray().Select(x => x!.GetValue<string>()).Order(StringComparer.Ordinal);
        Assert.Equal(expected, CollectionDataSemanticValidator.ReportCodes.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("globalReportRate", "acceptedPerMinute")]
    [InlineData("globalReportRate", "burst")]
    [InlineData("reporterReportRate", "acceptedPerMinute")]
    [InlineData("reporterReportRate", "burst")]
    public void CapabilityRatesMustMatchBothPinnedValues(string key, string field)
    {
        var root = JsonNode.Parse(ContractFixture.Read("capabilities-valid.json"))!;
        Assert.True(CollectionDataSemanticValidator.ValidateCapabilities(root.ToJsonString()).Success);
        root["limits"]![key]![field] = root["limits"]![key]![field]!.GetValue<int>() + 1;
        var result = CollectionDataSemanticValidator.ValidateCapabilities(root.ToJsonString());
        Assert.False(result.Success);
        Assert.Equal(CollectionDataValidationCode.SemanticValidationFailed, result.Code);
    }

    [Theory]
    [InlineData("Unlocks")]
    [InlineData("CurrentItemPresence")]
    public void EveryChangedSourceMustMatchAReportedType(string type)
    {
        var root = JsonNode.Parse(ContractFixture.Read("snapshot-changed-valid.json"))!;
        root["changedSources"] = new JsonArray("inventory", "mounts");
        root["changedDataTypes"] = new JsonArray("SourceHealth");
        Assert.True(CollectionDataSemanticValidator.ValidateChanged(root.ToJsonString()).Success);
        root["changedDataTypes"] = new JsonArray(type);
        var result = CollectionDataSemanticValidator.ValidateChanged(root.ToJsonString());
        Assert.False(result.Success);
        Assert.Equal(CollectionDataValidationCode.SemanticValidationFailed, result.Code);
    }

    [Theory]
    [InlineData("extra-fact")]
    [InlineData("wrong-source")]
    [InlineData("wrong-subject")]
    public void QuestEvidenceRequiresOneMatchingFact(string change)
    {
        var root = JsonNode.Parse(ContractFixture.Read("expanded-valid-beasts-populated.json"))!;
        AssertRich(root, true);
        var facts = root["collections"]!["quests"]!["items"]![0]!["ownershipEvidence"]!["facts"]!.AsArray();
        if (change == "extra-fact") facts.Add(facts[0]!.DeepClone());
        else if (change == "wrong-source") facts[0]!["sourceKey"] = "achievements";
        else facts[0]!["subjectId"] = facts[0]!["subjectId"]!.GetValue<uint>() + 1;
        AssertRich(root, false);
    }

    private static void AssertRich(JsonNode root, bool accepted)
    {
        using var document = JsonDocument.Parse(root.ToJsonString());
        Assert.Equal(accepted, CollectionDataSemanticValidator.ValidateRich(document.RootElement));
        Assert.Equal(accepted, CollectionDataSemanticValidator.ValidateRich(root.ToJsonString()).Success);
    }

    [Theory]
    [InlineData("1", "\"1\"")]
    [InlineData("null", "[]")]
    [InlineData("true", "1")]
    public void ConstantsRejectDifferentJsonKinds(string constant, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes("{\"$id\":\"https://example.test/collection-data.schema.json\",\"const\":" + constant + "}");
        var registry = new ContractSchemaRegistry([bytes]);
        using var valid = JsonDocument.Parse(constant);
        Assert.True(registry.Validate(valid.RootElement, "collection-data.schema.json"));
        using var invalid = JsonDocument.Parse(payload);
        Assert.False(registry.Validate(invalid.RootElement, "collection-data.schema.json"));
    }

    [Fact]
    public void SchemaSelectionRequiresTheCompleteFileName()
    {
        byte[][] resources =
        [
            Encoding.UTF8.GetBytes("{\"$id\":\"https://example.test/collection-data.schema.json\"}"),
            Encoding.UTF8.GetBytes("{\"$id\":\"https://example.test/not-other.schema.json\"}"),
        ];
        var registry = new ContractSchemaRegistry(resources);
        using var value = JsonDocument.Parse("7");
        Assert.True(registry.Validate(value.RootElement, "not-other.schema.json"));
        Assert.Throws<InvalidOperationException>(() => registry.Validate(value.RootElement, "other.schema.json"));
    }
}
