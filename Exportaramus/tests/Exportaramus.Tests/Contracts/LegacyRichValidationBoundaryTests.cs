using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Tests.Snapshots;

namespace Exportaramus.Tests.Contracts;

public sealed class LegacyRichValidationBoundaryTests
{
    private static JsonObject Rich() => JsonNode.Parse(CollectionDataSerializer.Rich(SnapshotFixture.Create(), "Fixture", SnapshotFixture.Now))!.AsObject();

    [Fact]
    public void MissingRichVersionReturnsTheVersionFailureCode()
    {
        var root = Rich();
        Assert.True(CollectionDataSemanticValidator.ValidateRich(root.ToJsonString()).Success);
        root.Remove("schemaVersion");
        Assert.Equal(CollectionDataValidationCode.MissingSchemaVersion,
            CollectionDataSemanticValidator.ValidateRich(root.ToJsonString()).Code);
    }

    [Fact]
    public void EmbeddedNullInVersionIsMalformedRatherThanUnsupported()
    {
        var root = Rich();
        Assert.True(CollectionDataSemanticValidator.ValidateRich(root.ToJsonString()).Success);
        root["schemaVersion"] = "1\0.0";
        Assert.Equal(CollectionDataValidationCode.MalformedSchemaVersion,
            CollectionDataSemanticValidator.ValidateRich(root.ToJsonString()).Code);
    }

    [Fact]
    public void RelicFactsMustEachMatchTheirSourceDataType()
    {
        var root = Rich();
        using (var baseline = JsonDocument.Parse(root.ToJsonString()))
            Assert.True(CollectionDataSemanticValidator.ValidateRich(baseline.RootElement));
        var facts = root["collections"]!["relics"]!["items"]![0]!["ownershipEvidence"]!["facts"]!.AsArray();
        Assert.Contains(facts, fact => fact!["dataType"]!.GetValue<string>() == "CurrentItemPresence");
        facts.Single(fact => fact!["sourceKey"]!.GetValue<string>() == "achievements")!["dataType"] = "ReportedOwnership";
        using var changed = JsonDocument.Parse(root.ToJsonString());
        Assert.False(CollectionDataSemanticValidator.ValidateRich(changed.RootElement));
    }
}
