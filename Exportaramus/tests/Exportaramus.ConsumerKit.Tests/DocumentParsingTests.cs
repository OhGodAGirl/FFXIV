using System.Text.Json;
using Exportaramus.ConsumerKit.V1_2;

namespace Exportaramus.ConsumerKit.Tests;

public sealed class DocumentParsingTests
{
    [Theory]
    [InlineData(null, CollectionDataValidationCode.NoPayload)]
    [InlineData("", CollectionDataValidationCode.NoPayload)]
    [InlineData(" ", CollectionDataValidationCode.InvalidJson)]
    [InlineData("{\"a\":1,}", CollectionDataValidationCode.InvalidJson)]
    [InlineData("{/*comment*/\"a\":1}", CollectionDataValidationCode.InvalidJson)]
    [InlineData("{\"a\":1,\"a\":2}", CollectionDataValidationCode.InvalidJson)]
    [InlineData("{\"outer\":{\"a\":1,\"a\":2}}", CollectionDataValidationCode.InvalidJson)]
    [InlineData("[{\"a\":1,\"a\":2}]", CollectionDataValidationCode.InvalidJson)]
    public void InvalidDocumentsReturnTheSpecificFailure(string? payload, CollectionDataValidationCode expected)
    {
        var result = CollectionDataSemanticValidator.ParseDocument(payload, 4096, false);
        Assert.False(result.Success);
        Assert.Equal(expected, result.Code);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData("{}", 2, true)]
    [InlineData("{}", 1, false)]
    [InlineData("\"é\"", 4, true)]
    [InlineData("\"é\"", 3, false)]
    public void PayloadLimitCountsUtf8BytesAndAcceptsTheExactBoundary(string payload, int limit, bool accepted)
    {
        var result = CollectionDataSemanticValidator.ParseDocument(payload, limit, false);
        using var document = result.Value;
        Assert.Equal(accepted, result.Success);
        Assert.Equal(accepted ? CollectionDataValidationCode.None : CollectionDataValidationCode.PayloadTooLarge, result.Code);
        if (accepted) Assert.Equal(payload, document!.RootElement.GetRawText());
        else Assert.Null(document);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":12}")]
    [InlineData("{\"schemaVersion\":null}")]
    public void VersionedDocumentsRequireAStringVersion(string payload)
    {
        var result = CollectionDataSemanticValidator.ParseDocument(payload, 4096, true);
        Assert.False(result.Success);
        Assert.Equal(CollectionDataValidationCode.MissingSchemaVersion, result.Code);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.2.3")]
    [InlineData(".2")]
    [InlineData("1.")]
    [InlineData("-1.2")]
    [InlineData("1.+2")]
    [InlineData("x.2")]
    [InlineData("1.x")]
    [InlineData("4294967296.2")]
    [InlineData("1.4294967296")]
    public void VersionComponentsMustBeUnsignedDecimalIntegers(string version)
    {
        var payload = JsonSerializer.Serialize(new { schemaVersion = version });
        var result = CollectionDataSemanticValidator.ParseDocument(payload, 4096, true);
        Assert.False(result.Success);
        Assert.Equal(CollectionDataValidationCode.MalformedSchemaVersion, result.Code);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData("1.1")]
    [InlineData("2.0")]
    [InlineData("01.2")]
    [InlineData("1.02")]
    [InlineData("4294967295.2")]
    public void WellFormedVersionsMustMatchTheSupportedVersionExactly(string version)
    {
        var result = CollectionDataSemanticValidator.ParseDocument(JsonSerializer.Serialize(new { schemaVersion = version }), 4096, true);
        Assert.False(result.Success);
        Assert.Equal(CollectionDataValidationCode.UnsupportedSchemaVersion, result.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public void SupportedVersionPreservesTheDocument()
    {
        const string payload = "{\"schemaVersion\":\"1.2\",\"value\":42}";
        var result = CollectionDataSemanticValidator.ParseDocument(payload, 4096, true);
        using var document = result.Value;
        Assert.True(result.Success);
        Assert.Equal(CollectionDataValidationCode.None, result.Code);
        Assert.Equal(42, document!.RootElement.GetProperty("value").GetInt32());
    }

    [Fact]
    public void InvalidUtf16IsRejectedBeforeJsonParsing()
    {
        var result = CollectionDataSemanticValidator.ParseDocument("\"\ud800\"", 4096, false);
        Assert.False(result.Success);
        Assert.Equal(CollectionDataValidationCode.InvalidJson, result.Code);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void NestingDepthHasAnInclusiveLimit(int depth, bool accepted)
    {
        var payload = new string('[', depth) + "0" + new string(']', depth);
        var result = CollectionDataSemanticValidator.ParseDocument(payload, 4096, false);
        using var document = result.Value;
        Assert.Equal(accepted, result.Success);
        Assert.Equal(accepted ? CollectionDataValidationCode.None : CollectionDataValidationCode.InvalidJson, result.Code);
    }

    [Fact]
    public void RepeatedNamesInSeparateObjectsAreNotDuplicates()
    {
        var result = CollectionDataSemanticValidator.ParseDocument("[{\"a\":1},{\"a\":2}]", 4096, false);
        using var document = result.Value;
        Assert.True(result.Success);
        Assert.Equal(2, document!.RootElement.GetArrayLength());
    }
}
