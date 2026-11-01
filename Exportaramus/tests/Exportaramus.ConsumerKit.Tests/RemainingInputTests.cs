using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_2;
namespace Exportaramus.ConsumerKit.Tests;

public sealed class RemainingInputTests
{
    [Fact]
    public void RichBeastsMustBeRecognized()
    {
        var root = JsonNode.Parse(ContractFixture.Read("expanded-valid-beasts-populated.json"))!;
        using (var baseline = JsonDocument.Parse(root.ToJsonString()))
            Assert.True(CollectionDataSemanticValidator.ValidateRich(baseline.RootElement));
        var item = root["collections"]!["beasts"]!["items"]![0]!;
        item["id"] = uint.MaxValue;
        item["ownershipEvidence"]!["facts"]![0]!["subjectId"] = uint.MaxValue;
        using var changed = JsonDocument.Parse(root.ToJsonString());
        Assert.False(CollectionDataSemanticValidator.ValidateRich(changed.RootElement));
    }
    [Fact]
    public void InvalidUnicodeIsRejectedBeforeByteLengthFallback()
    {
        var payload = new string(['"', '\uD800', '"']);
        var result = CollectionDataSemanticValidator.ParseDocument(payload, 3, false);
        Assert.False(result.Success);
        Assert.Equal(CollectionDataValidationCode.InvalidJson, result.Code);
    }
    [Fact]
    public void RichPayloadReportsMissingVersionBeforeContractFailure()
    {
        var result = CollectionDataSemanticValidator.ValidateRich("{}");
        Assert.False(result.Success);
        Assert.Equal(CollectionDataValidationCode.MissingSchemaVersion, result.Code);
    }
}
