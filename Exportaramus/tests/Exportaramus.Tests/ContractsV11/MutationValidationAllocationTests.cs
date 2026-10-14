// Regression parity with Contracts/MutationValidationAllocationTests.cs, targeting the independently pinned 1.1 assembly.
using Exportaramus.ConsumerKit.V1_1;
using Exportaramus.Tests.Performance;
using System.Text;
using System.Text.Json;
using Xunit.Abstractions;

namespace Exportaramus.Tests.ContractsV11;

[Collection(OfflinePerformanceCollection.Name)]
public sealed class MutationValidationAllocationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task DistinctNestedValuesDoNotDegenerateIntoPairwiseComparisons()
    {
        var schema = Encoding.UTF8.GetBytes("{\"$id\":\"https://exportaramus.local/schemas/1.0/collection-data.schema.json\",\"type\":\"array\",\"uniqueItems\":true}");
        var payload = JsonSerializer.Serialize(Enumerable.Range(0, 40000).Select(value => new[] { value }));
        Assert.InRange(Encoding.UTF8.GetByteCount(payload), 1, 1048576);
        var validation = Task.Run(() =>
        {
            var registry = new ContractSchemaRegistry([schema]);
            using var document = JsonDocument.Parse(payload);
            return registry.Validate(document.RootElement, "collection-data.schema.json");
        });
        Assert.True(await validation.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData("duplicate", CollectionDataValidationCode.InvalidJson)]
    [InlineData("missing", CollectionDataValidationCode.MissingSchemaVersion)]
    [InlineData("malformed", CollectionDataValidationCode.MalformedSchemaVersion)]
    [InlineData("unsupported", CollectionDataValidationCode.UnsupportedSchemaVersion)]
    [InlineData("invalid-unicode", CollectionDataValidationCode.InvalidJson)]
    public void RepeatedRejectedDocumentsReturnTheirLargeParsingBuffers(string defect, CollectionDataValidationCode expected)
    {
        var prefix = defect switch
        {
            "duplicate" => "\"schemaVersion\":\"1.0\",\"schemaVersion\":\"1.0\",",
            "missing" => "",
            "malformed" => "\"schemaVersion\":\"1\",",
            "unsupported" => "\"schemaVersion\":\"2.0\",",
            _ => "\"schemaVersion\":\"\\uD800\",",
        };
        var payload = "{" + prefix + "\"padding\":\"" + new string('a', 262144) + "\"}";
        for (var index = 0; index < 3; index++) Assert.Equal(expected, CollectionDataSemanticValidator.ParseDocument(payload, 1048576, true).Code);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 8; index++) Assert.Equal(expected, CollectionDataSemanticValidator.ParseDocument(payload, 1048576, true).Code);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"Eight rejected {defect} documents allocated {allocated} bytes after warmup.");
        // Returning pooled JSON buffers keeps repeated failures below even two full input buffers.
        Assert.InRange(allocated, 0, 524288);
    }
}
