// Regression parity with Contracts/SchemaVocabularyTests.cs, targeting the independently pinned 1.2 assembly.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_2;

namespace Exportaramus.Tests.ContractsV12;

public sealed class SchemaVocabularyTests
{
    [Theory]
    [InlineData("{\"type\":\"string\",\"minLength\":2,\"maxLength\":3}", "\"ab\"", true)]
    [InlineData("{\"type\":\"string\",\"minLength\":2}", "\"a\"", false)]
    [InlineData("{\"type\":\"string\",\"maxLength\":3}", "\"abcd\"", false)]
    [InlineData("{\"type\":\"string\",\"maxLength\":1}", "\"\ud83d\ude00\"", true)]
    [InlineData("{\"type\":\"number\",\"minimum\":1,\"maximum\":2}", "1.5", true)]
    [InlineData("{\"type\":\"number\",\"minimum\":1}", "0", false)]
    [InlineData("{\"type\":\"number\",\"maximum\":2}", "3", false)]
    [InlineData("{\"type\":\"number\",\"exclusiveMinimum\":1}", "1", false)]
    [InlineData("{\"type\":\"number\",\"exclusiveMinimum\":1}", "2", true)]
    [InlineData("{\"type\":\"number\",\"exclusiveMaximum\":2}", "2", false)]
    [InlineData("{\"type\":\"number\",\"exclusiveMaximum\":2}", "1", true)]
    [InlineData("{\"type\":\"integer\"}", "1.5", false)]
    [InlineData("{\"type\":\"integer\"}", "1.0", true)]
    [InlineData("{\"type\":\"number\"}", "1e1000", true)]
    [InlineData("{\"type\":\"null\"}", "null", true)]
    [InlineData("{\"type\":\"boolean\"}", "false", true)]
    [InlineData("{\"type\":\"unsupported\"}", "true", false)]
    [InlineData("{\"type\":[\"null\",\"string\"]}", "false", false)]
    [InlineData("{\"const\":3}", "2", false)]
    [InlineData("{\"enum\":[1,2]}", "3", false)]
    [InlineData("{\"allOf\":[{\"type\":\"string\"},{\"minLength\":2}]}", "\"x\"", false)]
    [InlineData("{\"anyOf\":[{\"type\":\"string\"},{\"type\":\"null\"}]}", "null", true)]
    [InlineData("{\"anyOf\":[{\"type\":\"string\"},{\"type\":\"null\"}]}", "0", false)]
    [InlineData("{\"oneOf\":[{\"type\":\"number\"},{\"minimum\":0}]}", "1", false)]
    [InlineData("{\"oneOf\":[{\"type\":\"string\"},{\"type\":\"null\"}]}", "null", true)]
    [InlineData("{\"not\":{\"type\":\"number\"}}", "1", false)]
    [InlineData("{\"if\":{\"type\":\"number\"},\"then\":{\"minimum\":2},\"else\":{\"type\":\"string\"}}", "1", false)]
    [InlineData("{\"if\":{\"type\":\"number\"},\"then\":{\"minimum\":2},\"else\":{\"type\":\"string\"}}", "null", false)]
    [InlineData("{\"if\":{\"type\":\"number\"},\"then\":{\"minimum\":2}}", "null", true)]
    [InlineData("{\"type\":\"array\",\"minItems\":1}", "[]", false)]
    [InlineData("{\"type\":\"array\",\"maxItems\":1}", "[1,2]", false)]
    [InlineData("{\"type\":\"array\",\"items\":{\"type\":\"number\"}}", "[1,\"a\"]", false)]
    [InlineData("{\"type\":\"array\",\"uniqueItems\":true}", "[1,1.0]", false)]
    [InlineData("{\"type\":\"array\",\"uniqueItems\":false}", "[1,1]", true)]
    [InlineData("{\"type\":\"array\",\"uniqueItems\":true}", "[{\"a\":1,\"b\":[2]},{\"b\":[2],\"a\":1}]", false)]
    [InlineData("{\"type\":\"object\",\"minProperties\":1}", "{}", false)]
    [InlineData("{\"type\":\"object\",\"maxProperties\":1}", "{\"a\":1,\"b\":2}", false)]
    [InlineData("{\"type\":\"object\",\"required\":[\"a\"]}", "{}", false)]
    [InlineData("{\"type\":\"object\",\"additionalProperties\":false}", "{\"a\":1}", false)]
    [InlineData("{\"type\":\"object\",\"additionalProperties\":true}", "{\"a\":1}", true)]
    [InlineData("{\"type\":\"object\",\"additionalProperties\":{\"type\":\"number\"}}", "{\"a\":false}", false)]
    [InlineData("{\"type\":\"object\",\"properties\":{\"a\":{\"type\":\"number\"}}}", "{\"a\":false}", false)]
    [InlineData("{\"format\":\"uuid\"}", "\"invalid\"", false)]
    [InlineData("{\"format\":\"date-time\"}", "\"2026-02-30T00:00:00Z\"", false)]
    [InlineData("{\"format\":\"date-time\"}", "\"not-a-timestamp\"", false)]
    [InlineData("{\"pattern\":\"^a+$\"}", "\"bbb\"", false)]
    public void IndependentSchemaVocabularyVectors(string body, string input, bool expected)
    {
        var schema = JsonNode.Parse(body)!.AsObject(); schema["$id"] = "https://exportaramus.local/schemas/1.0/collection-data.schema.json";
        var registry = new ContractSchemaRegistry([Encoding.UTF8.GetBytes(schema.ToJsonString())]);
        using var document = JsonDocument.Parse(input);
        Assert.Equal(expected, registry.Validate(document.RootElement, "collection-data.schema.json"));
    }

    [Fact]
    public void MissingCompactSchemaFailsClosed()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => new ContractSchemaRegistry([]));
        Assert.False(string.IsNullOrWhiteSpace(failure.Message));
    }

    [Fact]
    public void MissingLocalReferenceFailsClosed()
    {
        var schema = new JsonObject { ["$id"] = "https://exportaramus.local/schemas/1.0/collection-data.schema.json", ["$ref"] = "missing.schema.json" };
        var registry = new ContractSchemaRegistry([Encoding.UTF8.GetBytes(schema.ToJsonString())]);
        using var document = JsonDocument.Parse("{}");
        Assert.False(registry.Validate(document.RootElement, "collection-data.schema.json"));
    }

    [Theory]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void FiniteReferenceChainHonorsExactEvaluationDepth(int depth, bool expected)
    {
        var definitions = new JsonObject();
        for (var index = 1; index <= depth; index++)
            definitions["node" + index] = index == depth ? new JsonObject() : new JsonObject { ["$ref"] = "#/$defs/node" + (index + 1) };
        var schema = new JsonObject
        {
            ["$id"] = "https://exportaramus.local/schemas/1.0/collection-data.schema.json",
            ["$defs"] = definitions,
            ["$ref"] = "#/$defs/node1",
        };
        var resources = Encoding.UTF8.GetBytes(schema.ToJsonString());
        Exception? failure = null;
        // Mutation instrumentation enlarges evaluator stack frames; the tested depth limit remains 128.
        var thread = new Thread(() =>
        {
            try
            {
                var registry = new ContractSchemaRegistry([resources]);
                using var document = JsonDocument.Parse("{}");
                Assert.Equal(expected, registry.Validate(document.RootElement, "collection-data.schema.json"));
            }
            catch (Exception exception) { failure = exception; }
        }, 32 * 1024 * 1024)
        { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Finite reference-chain evaluation did not terminate.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}


