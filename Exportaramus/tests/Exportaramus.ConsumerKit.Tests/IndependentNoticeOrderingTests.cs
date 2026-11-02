using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_2;
namespace Exportaramus.ConsumerKit.Tests;

public sealed class IndependentNoticeOrderingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EachNoticeArrayMustBeSortedIndependently(bool types)
    {
        var root = JsonNode.Parse(ContractFixture.Read("snapshot-changed-valid.json"))!.AsObject();
        root["changedDataTypes"] = new JsonArray("SourceHealth", "Unlocks");
        root["changedSources"] = new JsonArray("inventory", "mounts");
        Assert.True(CollectionDataSemanticValidator.ValidateChanged(root.ToJsonString()).Success);
        if (types) root["changedDataTypes"] = new JsonArray("Unlocks", "SourceHealth");
        else root["changedSources"] = new JsonArray("mounts", "inventory");
        var result = CollectionDataSemanticValidator.ValidateChanged(root.ToJsonString());
        Assert.False(result.Success);
        Assert.Equal(CollectionDataValidationCode.SemanticValidationFailed, result.Code);
    }
}
