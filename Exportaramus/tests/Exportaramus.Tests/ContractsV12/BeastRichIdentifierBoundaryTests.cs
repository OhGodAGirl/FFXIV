using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Tests.Snapshots;
using System.Text.Json.Nodes;
namespace Exportaramus.Tests.ContractsV12;
public sealed class BeastRichIdentifierBoundaryTests
{
    [Theory]
    [InlineData(1u, true)]
    [InlineData(50u, true)]
    [InlineData(51u, false)]
    public void RichBeastsRejectIdsOutsidePinnedCatalog(uint id, bool accepted)
    {
        var root = JsonNode.Parse(CollectionDataSerializer.RichV12(SnapshotFixture.Create(), "Test", SnapshotFixture.Now))!;
        var item = root["collections"]!["beasts"]!["items"]![0]!;
        item["id"] = id;
        item["ownershipEvidence"]!["facts"]![0]!["subjectId"] = id;
        Assert.Equal(accepted, Exportaramus.ConsumerKit.V1_2.CollectionDataSemanticValidator.ValidateRich(root.ToJsonString()).Success);
    }
}
