using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_2;

namespace Exportaramus.ConsumerKit.Tests;

public sealed class ChangedNoticeValidationTests
{
    [Theory]
    [InlineData("reportedOwnership", "ReportedOwnership", "Unlocks")]
    [InlineData("mounts", "Unlocks", "ReportedOwnership")]
    [InlineData("inventory", "CurrentItemPresence", "Unlocks")]
    [InlineData("reportedOwnership", "SourceHealth", "CurrentItemPresence")]
    [InlineData("reportedOwnership", "SourceCapabilities", "Unlocks")]
    [InlineData("mounts", "SourceHealth", "CurrentItemPresence")]
    [InlineData("inventory", "SourceCapabilities", "ReportedOwnership")]
    public void ChangedSourcesMustMatchTheReportedDataType(string source, string validType, string invalidType)
    {
        var root = JsonNode.Parse(ContractFixture.Read("snapshot-changed-valid.json"))!.AsObject();
        root["changedSources"] = new JsonArray(source);
        root["changedDataTypes"] = new JsonArray(validType);
        Assert.True(CollectionDataSemanticValidator.ValidateChanged(root.ToJsonString()).Success);

        root["changedDataTypes"] = new JsonArray(invalidType);
        var result = CollectionDataSemanticValidator.ValidateChanged(root.ToJsonString());
        Assert.False(result.Success);
        Assert.Equal(CollectionDataValidationCode.SemanticValidationFailed, result.Code);
    }
}
