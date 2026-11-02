using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_1;
using Exportaramus.Tests.Snapshots;

namespace Exportaramus.Tests.ContractsV11;

public sealed class VersionNullBoundaryTests
{
    private static JsonObject Rich() => JsonNode.Parse(V11FixtureAdapter.Rich(SnapshotFixture.Create(), "Fixture", SnapshotFixture.Now))!.AsObject();

    [Fact]
    public void EmbeddedNullInVersionIsMalformedRatherThanUnsupported()
    {
        var root = Rich();
        Assert.True(CollectionDataSemanticValidator.ValidateRich(root.ToJsonString()).Success);
        root["schemaVersion"] = "1\0.0";
        Assert.Equal(CollectionDataValidationCode.MalformedSchemaVersion,
            CollectionDataSemanticValidator.ValidateRich(root.ToJsonString()).Code);
    }

}
