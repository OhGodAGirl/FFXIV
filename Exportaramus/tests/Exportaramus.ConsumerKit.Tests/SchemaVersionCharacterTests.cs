using System.Text.Json;
using Exportaramus.ConsumerKit.V1_2;

namespace Exportaramus.ConsumerKit.Tests;

public sealed class SchemaVersionCharacterTests
{
    [Theory]
    [InlineData("1\0.2")]
    [InlineData("1.2\0")]
    public void SchemaVersionRejectsEmbeddedNullCharacters(string version)
    {
        var valid = CollectionDataSemanticValidator.ParseDocument("{\"schemaVersion\":\"1.2\"}", 4096, true);
        Assert.True(valid.Success);
        valid.Value!.Dispose();
        var result = CollectionDataSemanticValidator.ParseDocument(JsonSerializer.Serialize(new { schemaVersion = version }), 4096, true);
        Assert.False(result.Success);
        Assert.Equal(CollectionDataValidationCode.MalformedSchemaVersion, result.Code);
    }
}
