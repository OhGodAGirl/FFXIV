using System.Text;
using System.Text.Json;
using Exportaramus.ConsumerKit.V1_2;

namespace Exportaramus.ConsumerKit.Tests;

public sealed class SchemaAdditionalBoundaryTests
{
    private static bool Validate(string schema, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes("{\"$id\":\"https://example.test/collection-data.schema.json\"," + schema + "}");
        var registry = new ContractSchemaRegistry([bytes]);
        using var document = JsonDocument.Parse(payload);
        return registry.Validate(document.RootElement, "collection-data.schema.json");
    }

    [Theory]
    [InlineData("[10,100e-1]", false)]
    [InlineData("[10,1000e-1]", true)]
    [InlineData("[-10,-100e-1]", false)]
    [InlineData("[-10,-1000e-1]", true)]
    public void UniqueNumbersNormalizeExponentSignChanges(string payload, bool accepted) =>
        Assert.Equal(accepted, Validate("\"uniqueItems\":true", payload));

    [Theory]
    [InlineData("{\"a\":1,\"b\":2}", true)]
    [InlineData("{\"a\":1}", false)]
    [InlineData("{\"b\":2}", false)]
    [InlineData("{}", false)]
    public void RequiredPropertiesMustAllExist(string payload, bool accepted) =>
        Assert.Equal(accepted, Validate("\"required\":[\"a\",\"b\"]", payload));

    [Theory]
    [InlineData("{\"known\":7}", true)]
    [InlineData("{\"known\":7,\"extra\":8}", false)]
    [InlineData("{\"known\":7,\"\":8}", false)]
    public void AdditionalPropertiesRejectUnknownNames(string payload, bool accepted) =>
        Assert.Equal(accepted, Validate("\"properties\":{\"known\":{\"const\":7}},\"additionalProperties\":false", payload));

    [Theory]
    [InlineData("reference", 128, true)]
    [InlineData("reference", 129, false)]
    [InlineData("allOf", 64, true)]
    [InlineData("allOf", 65, false)]
    [InlineData("items", 127, true)]
    [InlineData("items", 128, false)]
    [InlineData("properties", 127, true)]
    [InlineData("properties", 128, false)]
    [InlineData("additionalProperties", 127, true)]
    [InlineData("additionalProperties", 128, false)]
    public void FiniteReferenceChainsRespectTheValidationDepth(string route, int links, bool accepted)
    {
        string Reference(string target) => "{\"$ref\":\"" + target + "\"}";
        var resources = new List<byte[]>();
        for (var i = -1; i < links; i++)
        {
            var id = i == -1 ? "collection-data.schema.json" : $"node{i}.schema.json";
            var child = i == links - 1 ? "{\"const\":7}" : Reference($"node{i + 1}.schema.json");
            var body = route == "allOf" && i != links - 1 ? "{\"allOf\":[" + child + "]}" : child;
            if (i == -1)
            {
                body = route switch
                {
                    "items" => "{\"items\":" + child + "}",
                    "properties" => "{\"properties\":{\"a\":" + child + "}}",
                    "additionalProperties" => "{\"additionalProperties\":" + child + "}",
                    _ => body,
                };
            }
            resources.Add(Encoding.UTF8.GetBytes("{\"$id\":\"https://example.test/" + id + "\"," + body[1..]));
        }
        var registry = new ContractSchemaRegistry(resources);
        var payload = route == "items" ? "[7]" : route is "properties" or "additionalProperties" ? "{\"a\":7}" : "7";
        using var document = JsonDocument.Parse(payload);
        Assert.Equal(accepted, SchemaProcessProbe.WithValidationStack(() => registry.Validate(document.RootElement, "collection-data.schema.json")));
    }
}
