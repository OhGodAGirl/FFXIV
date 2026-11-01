using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_2;

namespace Exportaramus.ConsumerKit.Tests;

public sealed class SchemaBoundaryTests
{
    private static bool Validate(string schema, string payload)
    {
        var definition = JsonNode.Parse(schema)!.AsObject();
        definition["$id"] = "https://exportaramus.local/schemas/1.2/collection-data.schema.json";
        var registry = new ContractSchemaRegistry([Encoding.UTF8.GetBytes(definition.ToJsonString())]);
        using var document = JsonDocument.Parse(payload);
        return registry.Validate(document.RootElement, "collection-data.schema.json");
    }

    [Theory]
    [InlineData("minimum", "1", true)]
    [InlineData("minimum", "0.999999999999999999999999999999", false)]
    [InlineData("maximum", "1", true)]
    [InlineData("maximum", "1.000000000000000000000000000001", false)]
    [InlineData("exclusiveMinimum", "1", false)]
    [InlineData("exclusiveMinimum", "1.000000000000000000000000000001", true)]
    [InlineData("exclusiveMaximum", "1", false)]
    [InlineData("exclusiveMaximum", "0.999999999999999999999999999999", true)]
    public void NumericLimitsUseExactValues(string keyword, string value, bool accepted) =>
        Assert.Equal(accepted, Validate("{\"" + keyword + "\":1}", value));

    [Theory]
    [InlineData("1e2147483648", "10e2147483647", true)]
    [InlineData("1e-2147483648", "10e2147483647", false)]
    [InlineData("-0e999999999999999999999", "0", true)]
    [InlineData("1.2300e+000000100", "123e98", true)]
    [InlineData("-1e100", "1e100", false)]
    [InlineData("1000e-100000000000000000000", "1e-99999999999999999997", true)]
    [InlineData("0.0001e100000000000000000000", "1e99999999999999999996", true)]
    [InlineData("1.0000000000000000000000000000001", "1", false)]
    [InlineData("1e-1000", "0", false)]
    public void DuplicateNumbersAreComparedWithoutRoundingOrExponentOverflow(string left, string right, bool equal) =>
        Assert.Equal(!equal, Validate("{\"uniqueItems\":true}", "[" + left + "," + right + "]"));

    [Theory]
    [InlineData("1e99999999999999999999", true)]
    [InlineData("1e-99999999999999999999", false)]
    [InlineData("-0e-99999999999999999999", true)]
    [InlineData("100e-2", true)]
    [InlineData("1.0000000000000000000000000000001", false)]
    public void IntegerValidationDoesNotRoundFractions(string value, bool accepted) =>
        Assert.Equal(accepted, Validate("{\"type\":\"integer\"}", value));

    [Theory]
    [InlineData("{\"minLength\":1,\"maxLength\":1}", "\"\\ud83d\\ude00\"", true)]
    [InlineData("{\"minLength\":1,\"maxLength\":1}", "\"\"", false)]
    [InlineData("{\"minLength\":1,\"maxLength\":1}", "\"ab\"", false)]
    [InlineData("{\"minItems\":1,\"maxItems\":1}", "[0]", true)]
    [InlineData("{\"minItems\":1,\"maxItems\":1}", "[]", false)]
    [InlineData("{\"minItems\":1,\"maxItems\":1}", "[0,1]", false)]
    [InlineData("{\"minProperties\":1,\"maxProperties\":1}", "{\"a\":0}", true)]
    [InlineData("{\"minProperties\":1,\"maxProperties\":1}", "{}", false)]
    [InlineData("{\"minProperties\":1,\"maxProperties\":1}", "{\"a\":0,\"b\":1}", false)]
    public void ContainerAndUnicodeLimitsAreInclusive(string schema, string value, bool accepted) =>
        Assert.Equal(accepted, Validate(schema, value));
}
