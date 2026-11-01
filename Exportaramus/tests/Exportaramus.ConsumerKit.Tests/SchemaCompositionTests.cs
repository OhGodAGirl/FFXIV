using System.Text;
using System.Text.Json;
using Exportaramus.ConsumerKit.V1_2;

namespace Exportaramus.ConsumerKit.Tests;

public sealed class SchemaCompositionTests
{
    private static bool Validate(string schema, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes("{\"$id\":\"https://example.test/collection-data.schema.json\"," + schema + "}");
        var registry = new ContractSchemaRegistry([bytes]);
        using var document = JsonDocument.Parse(payload);
        return registry.Validate(document.RootElement, "collection-data.schema.json");
    }

    [Theory]
    [InlineData("\"allOf\":[{\"minimum\":1},{\"maximum\":3}]", "2", true)]
    [InlineData("\"allOf\":[{\"minimum\":1},{\"maximum\":3}]", "0", false)]
    [InlineData("\"allOf\":[{\"minimum\":1},{\"maximum\":3}]", "4", false)]
    [InlineData("\"anyOf\":[{\"const\":1},{\"const\":3}]", "1", true)]
    [InlineData("\"anyOf\":[{\"const\":1},{\"const\":3}]", "3", true)]
    [InlineData("\"anyOf\":[{\"const\":1},{\"const\":3}]", "2", false)]
    [InlineData("\"oneOf\":[{\"minimum\":1},{\"maximum\":3}]", "0", true)]
    [InlineData("\"oneOf\":[{\"minimum\":1},{\"maximum\":3}]", "4", true)]
    [InlineData("\"oneOf\":[{\"minimum\":1},{\"maximum\":3}]", "2", false)]
    [InlineData("\"oneOf\":[{\"const\":1},{\"const\":3}]", "2", false)]
    [InlineData("\"not\":{\"const\":1}", "1", false)]
    [InlineData("\"not\":{\"const\":1}", "2", true)]
    [InlineData("\"items\":true", "[1,2]", true)]
    [InlineData("\"items\":false", "[1]", false)]
    [InlineData("\"items\":false", "[]", true)]
    [InlineData("\"type\":[\"null\",\"string\"]", "null", true)]
    [InlineData("\"type\":[\"null\",\"string\"]", "\"x\"", true)]
    [InlineData("\"type\":[\"null\",\"string\"]", "1", false)]
    public void CombinationsRequireTheDeclaredMatches(string schema, string payload, bool accepted) =>
        Assert.Equal(accepted, Validate(schema, payload));

    [Theory]
    [InlineData("2", true)]
    [InlineData("3", false)]
    [InlineData("-2", true)]
    [InlineData("-3", false)]
    public void ConditionalsValidateTheSelectedBranch(string payload, bool accepted) =>
        Assert.Equal(accepted, Validate("\"if\":{\"minimum\":0},\"then\":{\"const\":2},\"else\":{\"const\":-2}", payload));

    [Theory]
    [InlineData("\"$defs\":{\"a/b~c\":{\"const\":7}},\"$ref\":\"#/$defs/a~1b~0c\"", "7", true)]
    [InlineData("\"$defs\":{\"a/b~c\":{\"const\":7}},\"$ref\":\"#/$defs/a~1b~0c\"", "8", false)]
    [InlineData("\"$ref\":\"missing.schema.json\"", "7", false)]
    [InlineData("\"$ref\":\"#\"", "7", false)]
    public void ReferencesResolveEscapedNamesAndRejectMissingOrRecursiveTargets(string schema, string payload, bool accepted) =>
        Assert.Equal(accepted, Validate(schema, payload));

    [Theory]
    [InlineData("0", "-1", false)]
    [InlineData("0", "0", true)]
    [InlineData("0", "1", true)]
    [InlineData("1", "0", false)]
    [InlineData("-1", "0", true)]
    [InlineData("-1.2", "-1.21", false)]
    [InlineData("-1.2", "-1.19", true)]
    [InlineData("1.2", "1.19", false)]
    [InlineData("1.2", "1.21", true)]
    [InlineData("1e-10", "10e-11", true)]
    [InlineData("1e-10", "9e-11", false)]
    [InlineData("1e100", "1e99", false)]
    [InlineData("1e100", "1e101", true)]
    [InlineData("1e-100", "1e-101", false)]
    [InlineData("1e-100", "1e-99", true)]
    [InlineData("1", "-1", false)]
    [InlineData("-1", "1", true)]
    [InlineData("0.001", "1", true)]
    [InlineData("1", "0.001", false)]
    [InlineData("-0.001", "-1", false)]
    [InlineData("-1", "-0.001", true)]
    [InlineData("0.11", "0.1", false)]
    [InlineData("0.1", "0.11", true)]
    [InlineData("-0.11", "-0.1", true)]
    [InlineData("-0.1", "-0.11", false)]
    [InlineData("0.00123", "1.23e-3", true)]
    [InlineData("0.00124", "1.23e-3", false)]
    public void MinimumComparesSignsFractionsAndExponents(string minimum, string payload, bool accepted) =>
        Assert.Equal(accepted, Validate("\"minimum\":" + minimum, payload));
}
