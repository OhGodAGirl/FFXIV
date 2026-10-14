// Regression parity with Contracts/ExactJsonNumberTests.cs, targeting the independently pinned 1.1 assembly.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_1;

namespace Exportaramus.Tests.ContractsV11;

public sealed class ExactJsonNumberTests
{
    private static bool Check(string assertions, string input)
    {
        var schema = JsonNode.Parse(assertions)!.AsObject();
        schema["$id"] = "https://exportaramus.local/schemas/1.0/collection-data.schema.json";
        var registry = new ContractSchemaRegistry([Encoding.UTF8.GetBytes(schema.ToJsonString())]);
        using var value = JsonDocument.Parse(input);
        return registry.Validate(value.RootElement, "collection-data.schema.json");
    }

    [Theory]
    [InlineData("1.0000000000000000000000000000000000001")]
    [InlineData("4294967295.0000000000000000000000000000001")]
    public void ApprovedImportIntegerDomainNeverRoundsFractions(string number)
    {
        using var input = JsonDocument.Parse("{\"cards\":[" + number + "]}");
        Assert.False(ContractSchemaRegistry.Default.Validate(input.RootElement, "import.schema.json"));
    }

    [Theory]
    [InlineData("1e-1000", true)]
    [InlineData("16.0000000000000000000000000000001", false)]
    public void ApprovedDiagnosticsBoundsCompareExactValues(string number, bool expected)
    {
        var path = Path.Combine(ConsumerContractTests.Root, "tests", "fixtures", "Diagnostics", "1.0", "fixtures", "valid", "diagnostics-valid-ready.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!;
        root["ui"]!["uiScale"] = JsonNode.Parse(number);
        using var input = JsonDocument.Parse(root.ToJsonString());
        Assert.Equal(expected, new ContractSchemaRegistry([File.ReadAllBytes(Path.Combine(ConsumerContractTests.Root, "data/Contracts/1.1/schemas/collection-data.schema.json")), File.ReadAllBytes(Path.Combine(ConsumerContractTests.Root, "data/Diagnostics/1.0/exportaramus-diagnostics.schema.json"))]).Validate(input.RootElement, "exportaramus-diagnostics.schema.json"));
    }

    [Fact]
    public void ApprovedExtensionNumbersCannotHideDuplicateReports()
    {
        var root = JsonNode.Parse(ConsumerContractTests.Fixture("compact-valid-full.json"))!;
        root["reportedOwnership"]!["items"] = JsonNode.Parse("""
            [{"itemId":1,"reportedAtUtc":"2026-09-06T00:00:00Z","reporter":"Example","extension":1e100},
             {"itemId":1,"reportedAtUtc":"2026-09-06T00:00:00Z","reporter":"Example","extension":10e99}]
            """);
        using var input = JsonDocument.Parse(root.ToJsonString());
        Assert.False(ContractSchemaRegistry.Default.Validate(input.RootElement, "collection-data.schema.json"));
        var result = CollectionDataSemanticValidator.ValidateSnapshot(root.ToJsonString(), root["contentId"]!.ToString());
        Assert.False(result.Success);
        Assert.Equal(CollectionDataValidationCode.ContractBindingFailed, result.Code);
    }

    [Theory]
    [InlineData("1e2147483648", "10e2147483647", true)]
    [InlineData("1e-2147483648", "10e2147483647", false)]
    [InlineData("0.1e-2147483648", "1e2147483647", false)]
    [InlineData("-0e999999999999999999999", "0", true)]
    [InlineData("1.2300e+000000100", "123e98", true)]
    [InlineData("-1e100", "1e100", false)]
    [InlineData("1000e-100000000000000000000", "1e-99999999999999999997", true)]
    [InlineData("0.0001e100000000000000000000", "1e99999999999999999996", true)]
    [InlineData("1.0000000000000000000000000000001", "1", false)]
    [InlineData("1e-1000", "2e-1000", false)]
    [InlineData("1e-1000", "0", false)]
    public void UniquenessUsesExactValueAcrossUnboundedExponentSpellings(string left, string right, bool equal) =>
        Assert.Equal(!equal, Check("{\"type\":\"array\",\"uniqueItems\":true}", "[" + left + "," + right + "]"));

    [Theory]
    [InlineData("{\"type\":\"integer\"}", "1e99999999999999999999", true)]
    [InlineData("{\"type\":\"integer\"}", "1e-99999999999999999999", false)]
    [InlineData("{\"type\":\"integer\"}", "-0e-99999999999999999999", true)]
    [InlineData("{\"minimum\":0}", "-1e-1000", false)]
    [InlineData("{\"minimum\":-1}", "-1.000000000000000000000000000001", false)]
    [InlineData("{\"maximum\":0}", "1e-1000", false)]
    [InlineData("{\"exclusiveMinimum\":0}", "1e-1000", true)]
    [InlineData("{\"exclusiveMaximum\":0}", "-1e-1000", true)]
    [InlineData("{\"maximum\":1.1}", "1.01", true)]
    [InlineData("{\"maximum\":1.01}", "1.1", false)]
    [InlineData("{\"const\":0}", "0e99999999999999999999", true)]
    [InlineData("{\"const\":1}", "1e99999999999999999999", false)]
    [InlineData("{\"enum\":[1,2]}", "2.000000000000000000000000000001", false)]
    [InlineData("{\"const\":{\"value\":1e99999999999999999999}}", "{\"value\":10e99999999999999999998}", true)]
    [InlineData("{\"enum\":[[1e99999999999999999999]]}", "[10e99999999999999999998]", true)]
    public void ReachedNumericAssertionsDoNotRoundOrOverflow(string schema, string value, bool expected) => Assert.Equal(expected, Check(schema, value));

    [Theory]
    [InlineData("{\"maximum\":1.000000000000000000000000000001}", "1", true)]
    [InlineData("{\"minimum\":1e-1000}", "1", true)]
    [InlineData("{\"maximum\":1e-1000}", "1e-1001", true)]
    [InlineData("{\"minimum\":1e-1000}", "1e-1001", false)]
    [InlineData("{\"const\":1}", "100e-2", true)]
    public void ExactOrderHandlesImplicitZeroDigitsAndExponentCancellation(string schema, string value, bool expected) => Assert.Equal(expected, Check(schema, value));

    [Fact]
    public void EquivalentFiniteCoefficientScalingsMatchIndependentDecimalOracle()
    {
        foreach (var coefficient in new[] { -999999, -101, -10, -1, 0, 1, 10, 101, 999999 })
            for (var exponent = -6; exponent <= 6; exponent++)
            {
                var factor = 1m;
                for (var i = 0; i < Math.Abs(exponent); i++) factor *= 10;
                var number = exponent < 0 ? coefficient / factor : coefficient * factor;
                var actual = (coefficient * 10).ToString(System.Globalization.CultureInfo.InvariantCulture) + "e" + (exponent - 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var expected = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Assert.True(Check("{\"const\":" + expected + "}", actual));
                Assert.Equal(decimal.Truncate(number) == number, Check("{\"type\":\"integer\"}", actual));
                Assert.Equal(number >= -1 && number <= 1, Check("{\"minimum\":-1,\"maximum\":1}", actual));
            }
    }

    [Fact]
    public void LargeExponentSpellingHasPayloadBoundedAllocationWithoutExpansion()
    {
        var nines = new string('9', 65536);
        var previous = new string('9', 65535) + "8";
        var assertions = "{\"type\":\"array\",\"uniqueItems\":true}";
        Assert.False(Check(assertions, "[1e100,10e99]"));
        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.False(Check(assertions, "[1e" + nines + ",10e" + previous + "]"));
        Assert.False(Check(assertions, "[-0e" + nines + ",0]"));
        Assert.True(Check(assertions, "[1e" + nines + ",1e" + previous + "]"));
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 1, 16 * 1024 * 1024);
    }

    [Fact]
    public void ManyDistinctUnderflowAndOverflowValuesStayDistinctAndFindLateDuplicate()
    {
        var numbers = Enumerable.Range(1000, 2048).SelectMany(e => new[] { "1e" + e, "1e-" + e }).ToArray();
        var payload = "[" + string.Join(',', numbers) + "]";
        var assertions = "{\"type\":\"array\",\"uniqueItems\":true}";
        Assert.True(Check(assertions, payload));
        Assert.False(Check(assertions, payload[..^1] + ",10e-3048]"));
    }

    [Theory]
    [InlineData("[\"a\",\"\\u0061\"]", false)]
    [InlineData("[\"/\",\"\\/\"]", false)]
    [InlineData("[\"\\\"\",\"\\u0022\"]", false)]
    [InlineData("[\"\\\\\",\"\\u005c\"]", false)]
    [InlineData("[\"😀\",\"\\ud83d\\ude00\"]", false)]
    [InlineData("[{\"a\":1,\"b\":\"/\"},{\"b\":\"\\/\",\"\\u0061\":1.0}]", false)]
    [InlineData("[1,\"1e0\"]", true)]
    [InlineData("[false,0,null,\"0\"]", true)]
    [InlineData("[{\"a\":1,\"a\":2},{\"a\":2,\"a\":1}]", true)]
    [InlineData("[{\"a\":1,\"b\":3,\"a\":2},{\"b\":3,\"a\":1,\"a\":2}]", false)]
    [InlineData("[{\"a\":1},{\"a\":2}]", true)]
    [InlineData("[{\"a\":1},{\"b\":1}]", true)]
    [InlineData("[{\"a\":{\"v\":1}},{\"a\":{\"v\":2}}]", true)]
    [InlineData("[{\"a\":[1,2]},{\"a\":[2,1]}]", true)]
    public void CanonicalEqualityPreservesStringsKindsAndRepeatedPropertyOrder(string input, bool unique) =>
        Assert.Equal(unique, Check("{\"type\":\"array\",\"uniqueItems\":true}", input));

    [Fact]
    public void FullConsumerStillRejectsDuplicateObjectPropertyNames()
    {
        var source = ConsumerContractTests.Fixture("compact-valid-full.json");
        var payload = source.Insert(source.IndexOf('{') + 1, "\"extension\":1,\"extension\":2,");
        var result = CollectionDataSemanticValidator.ValidateSnapshot(payload, "1");
        Assert.False(result.Success);
        Assert.Equal(CollectionDataValidationCode.InvalidJson, result.Code);
    }
    [Theory]
    [InlineData("{\"const\":1}", "\"1\"", false)]
    [InlineData("{\"enum\":[1,2]}", "true", false)]
    [InlineData("{\"const\":1}", "1e0", true)]
    [InlineData("{\"const\":1}", "1e-000", true)]
    [InlineData("{\"const\":1}", "1e+000", true)]
    [InlineData("{\"maximum\":0.02}", "0.1e-1", true)]
    [InlineData("{\"minimum\":0.005}", "0.1e-1", true)]
    [InlineData("{\"type\":\"array\",\"uniqueItems\":true}", "[0.1e-1,1e-2]", false)]
    public void NumericAssertionsPreserveKindsZeroExponentsAndNegativeExponentAddition(string schema, string value, bool expected) =>
        Assert.Equal(expected, Check(schema, value));

}
