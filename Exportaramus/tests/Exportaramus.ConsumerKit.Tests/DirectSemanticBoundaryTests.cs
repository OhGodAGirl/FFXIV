using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_2;
namespace Exportaramus.ConsumerKit.Tests;

public sealed class DirectSemanticBoundaryTests
{
    private static JsonNode Snapshot() => JsonNode.Parse(ContractFixture.Read("expanded-valid-beasts-populated.json"))!;
    private static bool Validate(JsonNode root)
    {
        using var document = JsonDocument.Parse(root.ToJsonString());
        return CollectionDataSemanticValidator.ValidateRich(document.RootElement);
    }
    [Theory]
    [InlineData("dataType")]
    [InlineData("supportsQuantity")]
    public void EveryCapabilityFieldMustMatchItsSource(string field)
    {
        var root = Snapshot();
        Assert.True(Validate(root));
        if (field == "dataType") root["sourceCapabilities"]!["inventory"]![field] = "PermanentUnlock";
        else root["sourceCapabilities"]!["inventory"]![field] = false;
        Assert.False(Validate(root));
    }
    [Theory]
    [InlineData("valid", true)]
    [InlineData("none-code", false)]
    [InlineData("blank-reason", false)]
    public void StaleBeastsRequireBothReasonFields(string change, bool expected)
    {
        var root = Snapshot();
        root["consistency"] = "BestEffort";
        var health = root["sourceHealth"]!["beasts"]!;
        health["sourceStatus"] = "Stale";
        health["reasonCode"] = "LoggedOut";
        health["reason"] = "Previously observed";
        var collection = root["collections"]!["beasts"]!;
        collection["sourceStatus"] = "Stale";
        collection["items"]![0]!["ownershipEvidence"]!["facts"]![0]!["sourceStatus"] = "Stale";
        Assert.True(Validate(root));
        if (change == "none-code") health["reasonCode"] = "None";
        if (change == "blank-reason") health["reason"] = " ";
        Assert.Equal(expected, Validate(root));
    }
    [Fact]
    public void NonArrayDresserEntriesAreRejectedWithoutThrowing()
    {
        var root = Snapshot();
        Assert.True(Validate(root));
        root["currentItemPresence"]!["glamourDresser"]!["dresserEntries"] = new JsonObject();
        Assert.False(Validate(root));
    }
    [Fact]
    public void CapabilitiesReportMissingVersionBeforeContractFailure()
    {
        var result = CollectionDataSemanticValidator.ValidateCapabilities("{}");
        Assert.False(result.Success);
        Assert.Equal(CollectionDataValidationCode.MissingSchemaVersion, result.Code);
    }
}
