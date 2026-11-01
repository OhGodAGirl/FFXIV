using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_2;

namespace Exportaramus.ConsumerKit.Tests;

public sealed class CompactCollectionValidationTests
{
    [Fact]
    public void SnapshotRejectsUnknownUnlockCollections()
    {
        var root = JsonNode.Parse(ContractFixture.Read("compact-valid-full.json"))!.AsObject();
        Assert.True(CollectionDataSemanticValidator.ValidateSnapshot(root.ToJsonString(), ContractFixture.Character).Success);

        root["unlocks"]!["unknown"] = root["unlocks"]!["mounts"]!.DeepClone();
        var result = CollectionDataSemanticValidator.ValidateSnapshot(root.ToJsonString(), ContractFixture.Character);
        Assert.False(result.Success);
        Assert.Equal(CollectionDataValidationCode.SemanticValidationFailed, result.Code);
    }
}
