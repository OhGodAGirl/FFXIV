using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1;
using Exportaramus.Features.Diagnostics;

namespace Exportaramus.Tests.Diagnostics;

public sealed class DiagnosticVersionTests
{
    private static DiagnosticReport Report => new()
    {
        GeneratedAtUtc = new(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc),
        CollectionPipeline = new()
        {
            Serializers = new("Ready", "Unavailable", "NotBuilt"),
            OutputBytes = new(100, 237, 0),
        },
    };

    [Fact]
    public void NewReportsEmitExpandedFieldsAndDisplayTheirExactValues()
    {
        var prepared = DiagnosticReportSerializer.Prepare(Report);
        DiagnosticReportSerializer.ValidateWireJson(prepared.Json);
        using var document = JsonDocument.Parse(prepared.Json);
        var root = document.RootElement;
        Assert.Equal("1.1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("1.0", root.GetProperty("redaction").GetProperty("schemaVersion").GetString());
        var pipeline = root.GetProperty("collectionPipeline");
        Assert.Equal("Unavailable", pipeline.GetProperty("serializers").GetProperty("expanded").GetString());
        Assert.Equal(237, pipeline.GetProperty("outputBytes").GetProperty("expanded").GetInt32());
        Assert.False(pipeline.GetProperty("serializers").TryGetProperty("compendium", out _));
        Assert.False(pipeline.GetProperty("outputBytes").TryGetProperty("compendium", out _));
        Assert.DoesNotContain("compendium", prepared.Json, StringComparison.OrdinalIgnoreCase);
        Assert.True(ContractSchemaRegistry.Default.Validate(root, "exportaramus-diagnostics-v1.1.schema.json"));
        var rows = prepared.Sections.SelectMany(section => section.Rows);
        Assert.Contains(rows, row => row.Label == "collectionPipeline.serializers.expanded" && row.Value == "Unavailable");
        Assert.Contains(rows, row => row.Label == "collectionPipeline.outputBytes.expanded" && row.Value == "237");
    }

    [Fact]
    public void RetiredReportIsRejectedEvenWithACorrectFingerprint()
    {
        var root = ReadCurrent();
        root["schemaVersion"] = "1.0";
        foreach (var field in new[] { "serializers", "outputBytes" })
        {
            var map = root["collectionPipeline"]![field]!.AsObject();
            map["compendium"] = map["expanded"]!.DeepClone();
            map.Remove("expanded");
        }

        var error = Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.ValidateWireJson(Rehash(root)));
        Assert.Equal("DiagnosticSchemaInvalid", error.Message);
    }

    [Theory]
    [InlineData("serializers", false)]
    [InlineData("outputBytes", false)]
    [InlineData("serializers", true)]
    [InlineData("outputBytes", true)]
    public void CurrentReportsRejectRetiredFieldNames(string field, bool replaceCurrentField)
    {
        var root = ReadCurrent();
        var map = root["collectionPipeline"]![field]!.AsObject();
        map["compendium"] = map["expanded"]!.DeepClone();
        if (replaceCurrentField) map.Remove("expanded");

        var error = Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.ValidateWireJson(Rehash(root)));
        Assert.Equal("DiagnosticSchemaInvalid", error.Message);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("1.2")]
    public void RehashedReportsRejectOtherVersions(string claimed)
    {
        var root = ReadCurrent();
        root["schemaVersion"] = claimed;
        var error = Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.ValidateWireJson(Rehash(root)));
        Assert.Equal("DiagnosticSchemaInvalid", error.Message);
    }

    [Fact]
    public void CurrentReportsRetainFingerprintAndRedactionChecks()
    {
        var root = ReadCurrent();
        root["fingerprintSha256"] = new string('0', 64);
        var fingerprintError = Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.ValidateWireJson(root.ToJsonString()));
        Assert.Equal("DiagnosticFingerprintMismatch", fingerprintError.Message);

        root["runtime"]!["dependencies"]!["contentId"] = "redacted";
        var redactionError = Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.ValidateWireJson(Rehash(root)));
        Assert.Equal("DiagnosticProhibitedField", redactionError.Message);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("1.2")]
    public void ProducerCannotLabelNewFieldsAsAnotherVersion(string version)
    {
        var error = Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.Prepare(Report with { SchemaVersion = version }));
        Assert.Equal("DiagnosticSchemaInvalid", error.Message);
    }

    private static JsonObject ReadCurrent() => JsonNode.Parse(DiagnosticReportSerializer.Prepare(Report).Json)!.AsObject();

    private static string Rehash(JsonObject root)
    {
        using var document = JsonDocument.Parse(root.ToJsonString());
        root["fingerprintSha256"] = Convert.ToHexStringLower(SHA256.HashData(DiagnosticReportSerializer.Canonical(document.RootElement, true)));
        return root.ToJsonString();
    }
}
