using System.Text;
using System.Text.Json;
using Exportaramus.ConsumerKit.V1_2;

namespace Exportaramus.ConsumerKit.Tests;

public sealed class SchemaRegistrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingCompactSchemaReportsTheMissingResource(bool includeUnrelatedSchema)
    {
        byte[][] resources = includeUnrelatedSchema
            ? [Encoding.UTF8.GetBytes("{\"$id\":\"https://example.test/other.schema.json\"}")]
            : [];
        var exception = Assert.Throws<InvalidOperationException>(() => new ContractSchemaRegistry(resources));
        Assert.Contains("compact schema", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("number", "1.5", true)]
    [InlineData("number", "-2", true)]
    [InlineData("number", "\"1.5\"", false)]
    [InlineData("number", "null", false)]
    [InlineData("unsupported", "1", false)]
    [InlineData("unsupported", "null", false)]
    public void DeclaredTypesRejectIncompatibleValues(string type, string payload, bool accepted) =>
        Assert.Equal(accepted, Validate("\"type\":\"" + type + "\"", payload));

    [Theory]
    [InlineData("date-time", "2026-11-01T12:00:00Z", true)]
    [InlineData("date-time", "2026-11-01T12:00:00.123+02:30", true)]
    [InlineData("date-time", "2026-02-30T12:00:00Z", false)]
    [InlineData("date-time", "2026-11-01", false)]
    [InlineData("date-time", "2026-11-01T12:00:00", false)]
    [InlineData("date-time", "2026-11-01T25:00:00Z", false)]
    [InlineData("uuid", "01234567-89ab-cdef-0123-456789abcdef", true)]
    [InlineData("uuid", "0123456789abcdef0123456789abcdef", false)]
    [InlineData("uuid", "not-a-guid", false)]
    public void FormatsValidateStructureAndValues(string format, string value, bool accepted) =>
        Assert.Equal(accepted, Validate("\"format\":\"" + format + "\"", JsonSerializer.Serialize(value)));

    private static bool Validate(string schema, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes("{\"$id\":\"https://example.test/collection-data.schema.json\"," + schema + "}");
        var registry = new ContractSchemaRegistry([bytes]);
        using var document = JsonDocument.Parse(payload);
        return registry.Validate(document.RootElement, "collection-data.schema.json");
    }
}
