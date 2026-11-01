using System.Text;
using System.Text.Json;
using Exportaramus.ConsumerKit.V1_2;

namespace Exportaramus.ConsumerKit.Tests;

public sealed class CanonicalArrayTests
{
    [Theory]
    [InlineData("[[1],[2]]", true)]
    [InlineData("[[1],[1]]", false)]
    [InlineData("[[1,2],[2,1]]", true)]
    public void UniqueNestedArraysCompareTheirElementsInOrder(string payload, bool accepted) =>
        Assert.Equal(accepted, Validate("\"uniqueItems\":true", payload));

    [Theory]
    [InlineData("[1]", "[2]", false)]
    [InlineData("[1,2]", "[2,1]", false)]
    [InlineData("[1,2]", "[1,2]", true)]
    public void ArrayConstantsCompareTheirElementsInOrder(string constant, string payload, bool accepted) =>
        Assert.Equal(accepted, Validate("\"const\":" + constant, payload));

    private static bool Validate(string schema, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes("{\"$id\":\"https://example.test/collection-data.schema.json\"," + schema + "}");
        var registry = new ContractSchemaRegistry([bytes]);
        using var document = JsonDocument.Parse(payload);
        return registry.Validate(document.RootElement, "collection-data.schema.json");
    }
}
