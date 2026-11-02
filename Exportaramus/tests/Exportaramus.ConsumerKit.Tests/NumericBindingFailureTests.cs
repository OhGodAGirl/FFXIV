using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_2;
namespace Exportaramus.ConsumerKit.Tests;

public sealed class NumericBindingFailureTests
{
    [Theory]
    [InlineData("compact")]
    [InlineData("dto")]
    [InlineData("rich")]
    [InlineData("import")]
    public void IntegerExponentSpellingReturnsAValidationFailureInsteadOfThrowing(string kind)
    {
        var name = kind == "dto" ? "snapshot-changed-valid.json" : kind == "compact" ? "compact-valid-full.json" : kind == "rich" ? "expanded-valid-beasts-populated.json" : "ffxiv-collect-valid-beasts-all-collections.json";
        var root = JsonNode.Parse(ContractFixture.Read(name))!.AsObject();
        if (kind == "import") root["beasts"] = new JsonArray(1);
        CollectionDataValidationCode Validate(string payload) => kind == "dto" ? CollectionDataSemanticValidator.ValidateChanged(payload).Code : kind == "compact" ? CollectionDataSemanticValidator.ValidateSnapshot(payload, ContractFixture.Character).Code : kind == "rich" ? CollectionDataSemanticValidator.ValidateRich(payload).Code : CollectionDataSemanticValidator.ValidateFFXIVCollect(payload).Code;
        Assert.Equal(CollectionDataValidationCode.None, Validate(root.ToJsonString()));
        if (kind == "dto") root["revision"] = JsonNode.Parse("1e0");
        else if (kind == "compact") root["unlocks"]!["mounts"]!["unlockedIds"]![0] = JsonNode.Parse("1e0");
        else if (kind == "rich") root["collections"]!["beasts"]!["items"]![0]!["id"] = JsonNode.Parse("1e0");
        else root["beasts"]![0] = JsonNode.Parse("1e0");
        Assert.Equal(CollectionDataValidationCode.ContractBindingFailed, Validate(root.ToJsonString()));
    }
}

