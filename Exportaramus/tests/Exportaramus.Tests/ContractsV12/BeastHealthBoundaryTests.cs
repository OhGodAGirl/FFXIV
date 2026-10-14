using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_2;
namespace Exportaramus.Tests.ContractsV12;

public sealed class BeastHealthBoundaryTests
{
    [Theory]
    [InlineData("Fresh", "Derived", "None", "", false)]
    [InlineData("Fresh", "Restored", "None", "", false)]
    [InlineData("Fresh", "Observed", "None", "", true)]
    [InlineData("Stale", "Observed", "None", "Retained", false)]
    [InlineData("Stale", "Observed", "CaptureFailed", " ", false)]
    [InlineData("Stale", "Observed", "CaptureFailed", "Retained", true)]
    public void BeastObservationRequiresActualObservationAndExplainedStaleness(string status, string provenance, string reasonCode, string reason, bool accepted)
    {
        var path = Path.Combine(ConsumerContractTests.Root, "tests/fixtures/Contracts/1.2/fixtures/valid/compact-valid-beasts-populated.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["consistency"] = "MixedObservationTimes";
        var health = root["sourceHealth"]!["beasts"]!;
        health["sourceStatus"] = status;
        health["provenanceType"] = provenance;
        health["reasonCode"] = reasonCode;
        health["reason"] = reason;
        Assert.Equal(accepted, CollectionDataSemanticValidator.ValidateSnapshot(root.ToJsonString(), "12345678901234567").Success);
        // The semantic layer independently guards provenance and stale reasons.
        // The public schema also rejects these invalid vectors before that layer runs.
        using var document = System.Text.Json.JsonDocument.Parse(health.ToJsonString());
        var guard = typeof(CollectionDataSemanticValidator).GetMethod("BeastHealth", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        Assert.Equal(accepted, (bool)guard.Invoke(null, [document.RootElement])!);
    }
}

