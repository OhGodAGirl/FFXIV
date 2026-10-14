using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.Bootstrap;
using Exportaramus.ConsumerKit.V1;

namespace Exportaramus.Tests.Contracts;

public sealed class MutationConsumerTests
{
    [Fact]
    public void NewRegistryLoadsExactEmbeddedSchemasWithoutDependingOnStaticInitialization()
    {
        var registry = new ContractSchemaRegistry();
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(EmbeddedProductData.Read("collection-data.schema.json"))), registry.CompactSchemaHash);
        foreach (var (file, schema) in new[]
        {
            ("compact-valid-full.json", "collection-data.schema.json"),
            ("capabilities-valid.json", "capabilities.schema.json"),
            ("snapshot-changed-valid.json", "snapshot-changed.schema.json"),
            ("snapshot-withdrawn-valid.json", "snapshot-withdrawn.schema.json"),
            ("compendium-valid-complete.json", "exportaramus-snapshot.schema.json"),
        })
        {
            using var valid = JsonDocument.Parse(ConsumerContractTests.Fixture(file));
            using var missing = JsonDocument.Parse("{}");
            Assert.True(registry.Validate(valid.RootElement, schema));
            Assert.False(registry.Validate(missing.RootElement, schema));
        }
    }

    [Theory]
    [InlineData("{\"type\":\"array\",\"uniqueItems\":true}", "[[1],[2]]", true)]
    [InlineData("{\"type\":\"array\",\"uniqueItems\":true}", "[[1],[1.0]]", false)]
    [InlineData("{\"$defs\":{\"slash/name\":{\"const\":1}},\"$ref\":\"#/$defs/slash~1name\"}", "1", true)]
    [InlineData("{\"$defs\":{\"~token\":{\"const\":1}},\"$ref\":\"#/$defs/~0token\"}", "2", false)]
    [InlineData("{\"format\":\"date-time\"}", "\"2026-09-06 12:00:00Z\"", false)]
    [InlineData("{\"format\":\"date-time\"}", "\"2026-09-06T12:00:00+01:00\"", true)]
    public void NestedValuesEscapedReferenceNamesAndTimestampShapePreserveSchemaMeaning(string body, string input, bool expected)
    {
        var schema = JsonNode.Parse(body)!.AsObject(); schema["$id"] = "https://exportaramus.local/schemas/1.0/collection-data.schema.json";
        var registry = new ContractSchemaRegistry([Encoding.UTF8.GetBytes(schema.ToJsonString())]);
        using var document = JsonDocument.Parse(input);
        Assert.Equal(expected, registry.Validate(document.RootElement, "collection-data.schema.json"));
    }

    [Fact]
    public void ValidResultHasNoFailureCode()
    {
        var valid = ConsumerContractTests.Fixture("compendium-valid-complete.json");
        var validated = CollectionDataSemanticValidator.ValidateRich(valid);
        Assert.True(validated.Success); Assert.Equal(CollectionDataValidationCode.None, validated.Code); Assert.Equal("", validated.Detail);
    }
}
