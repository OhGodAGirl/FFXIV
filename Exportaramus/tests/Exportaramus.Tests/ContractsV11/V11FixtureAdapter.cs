using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_1;
namespace Exportaramus.Tests.ContractsV11;

// Reuse established adversarial vectors without changing the frozen 1.0 or 1.1 artifacts.
internal static class V11FixtureAdapter
{
    public static string Rich(Exportaramus.Features.CollectionData.CollectionData data, string name, DateTimeOffset at) =>
        Adapt(Exportaramus.Features.CollectionData.Publication.CollectionDataSerializer.Rich(data, name, at), "rich");

    public static string Adapt(string payload, string name)
    {
        var root = JsonNode.Parse(payload)!.AsObject();
        if (name.Contains("version-", StringComparison.Ordinal))
        {
            if (root["schemaVersion"]?.GetValue<string>() == "1.1") root["schemaVersion"] = "1.0";
            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        root["schemaVersion"] = "1.1";
        if (root["sourceCapabilities"] is JsonObject capabilities)
        {
            capabilities["facewear"] = new JsonObject
            {
                ["dataType"] = "PermanentUnlock",
                ["capabilityState"] = "UnavailablePendingValidation",
                ["supportsQuantity"] = false,
                ["reasonCode"] = "PatchValidationRequired",
                ["reason"] = "Not observed in this vector."
            };
        }
        if (root["sourceHealth"] is JsonObject health)
        {
            health["facewear"] = new JsonObject
            {
                ["dataType"] = "PermanentUnlock",
                ["capabilityState"] = "UnavailablePendingValidation",
                ["sourceStatus"] = "NeverObserved",
                ["completeness"] = "Unknown",
                ["provenanceType"] = "None",
                ["captureGeneration"] = 0,
                ["reasonCode"] = "PatchValidationRequired",
                ["reason"] = "Not observed in this vector."
            };
        }
        if (root["omittedCollections"] is JsonObject omitted)
            omitted["facewear"] = new JsonObject { ["reasonCode"] = "PatchValidationRequired", ["message"] = "Not observed in this vector." };
        if (root["unlockKeys"] is JsonArray keys) keys.Add("facewear");
        if (root["gates"] is JsonObject gates)
            foreach (var gate in gates.ToArray()) gates[gate.Key] = gate.Value!.GetValue<string>().Replace(".CollectionData.", ".CollectionData.V1_1.", StringComparison.Ordinal);
        if (root["collectionDataSchema"] is JsonObject schema)
        {
            schema["id"] = "https://exportaramus.local/schemas/1.1/collection-data.schema.json";
            schema["sha256"] = CollectionDataSemanticValidator.CompactSchemaHash;
        }
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
