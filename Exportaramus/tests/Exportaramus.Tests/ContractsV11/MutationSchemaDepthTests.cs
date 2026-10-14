// Regression parity with Contracts/MutationSchemaDepthTests.cs, targeting the independently pinned 1.1 assembly.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_1;

namespace Exportaramus.Tests.ContractsV11;

public sealed class MutationSchemaDepthTests
{
    [Fact]
    public void SchemaFilenameResolutionCannotMatchALongerFilenameSuffix()
    {
        var required = Encoding.UTF8.GetBytes("{\"$id\":\"https://exportaramus.local/schemas/1.0/collection-data.schema.json\",\"type\":\"number\"}");
        var unrelated = Encoding.UTF8.GetBytes("{\"$id\":\"https://exportaramus.local/schemas/1.0/othercollection-data.schema.json\",\"type\":\"string\"}");
        var registry = new ContractSchemaRegistry([required, unrelated]);
        using var document = JsonDocument.Parse("1");
        Assert.True(registry.Validate(document.RootElement, "collection-data.schema.json"));
    }
    [Theory]
    [InlineData("allOf", 64, true)]
    [InlineData("allOf", 65, false)]
    [InlineData("items", 64, true)]
    [InlineData("items", 65, false)]
    [InlineData("properties", 64, true)]
    [InlineData("properties", 65, false)]
    [InlineData("additionalProperties", 64, true)]
    [InlineData("additionalProperties", 65, false)]
    public void RecursiveVocabularyConsumesTheSameBoundedEvaluationDepth(string keyword, int levels, bool accepted)
    {
        var definitions = new JsonObject();
        JsonNode input = JsonValue.Create(0)!;
        for (var index = levels; index >= 1; index--)
        {
            JsonObject child = index == levels ? new() : new() { ["$ref"] = "#/$defs/node" + (index + 1) };
            definitions["node" + index] = keyword switch
            {
                "allOf" => new JsonObject { [keyword] = new JsonArray(child) },
                "properties" => new JsonObject { [keyword] = new JsonObject { ["a"] = child } },
                _ => new JsonObject { [keyword] = child },
            };
            if (keyword == "items") input = new JsonArray(input);
            else if (keyword is "properties" or "additionalProperties") input = new JsonObject { ["a"] = input };
        }
        var schema = new JsonObject
        {
            ["$id"] = "https://exportaramus.local/schemas/1.0/collection-data.schema.json",
            ["$defs"] = definitions,
            ["$ref"] = "#/$defs/node1",
        };
        var bytes = Encoding.UTF8.GetBytes(schema.ToJsonString());
        var json = input.ToJsonString(new JsonSerializerOptions { MaxDepth = 256 });
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var registry = new ContractSchemaRegistry([bytes]);
                using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 256 });
                Assert.Equal(accepted, registry.Validate(document.RootElement, "collection-data.schema.json"));
            }
            catch (Exception exception) { failure = exception; }
        }, 32 * 1024 * 1024)
        { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Finite vocabulary evaluation did not terminate.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Theory]
    [InlineData("1", false)]
    [InlineData("2", true)]
    public void TrueConditionalEvaluatesItsThenConstraint(string input, bool accepted)
    {
        var schema = Encoding.UTF8.GetBytes("{\"$id\":\"https://exportaramus.local/schemas/1.0/collection-data.schema.json\",\"if\":{\"type\":\"number\"},\"then\":{\"minimum\":2},\"else\":{\"const\":\"ok\"}}");
        var registry = new ContractSchemaRegistry([schema]);
        using var document = JsonDocument.Parse(input);
        Assert.Equal(accepted, registry.Validate(document.RootElement, "collection-data.schema.json"));
    }
}
