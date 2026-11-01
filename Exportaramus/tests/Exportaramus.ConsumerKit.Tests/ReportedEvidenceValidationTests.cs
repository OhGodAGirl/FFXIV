using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_2;

namespace Exportaramus.ConsumerKit.Tests;

public sealed class ReportedEvidenceValidationTests
{
    private static JsonObject Report(uint id, string reporter) => new()
    {
        ["itemId"] = id,
        ["reporter"] = reporter,
        ["reportedAtUtc"] = "2026-09-06T12:00:00.0000000Z",
    };

    private static JsonObject Snapshot(JsonArray reports)
    {
        var root = JsonNode.Parse(ContractFixture.Read("expanded-valid-beasts-populated.json"))!.AsObject();
        root["collections"]!["glamour_dresser"]!["items"]![0]!["reportedEvidence"] = reports;
        return root;
    }

    private static void AssertAccepted(JsonObject root, bool accepted)
    {
        var json = root.ToJsonString();
        using var document = JsonDocument.Parse(json);
        Assert.Equal(accepted, CollectionDataSemanticValidator.ValidateRich(document.RootElement));
        Assert.Equal(accepted, CollectionDataSemanticValidator.ValidateRich(json).Success);
    }

    [Theory]
    [InlineData(" Reporter")]
    [InlineData("Reporter ")]
    [InlineData("Report\ner")]
    [InlineData("e\u0301")]
    public void ReporterNamesMustBeTrimmedNormalizedAndFreeOfControls(string reporter)
    {
        var reports = new JsonArray(Report(100, "Report er"));
        var root = Snapshot(reports);
        AssertAccepted(root, true);
        reports[0]!["reporter"] = reporter;
        AssertAccepted(root, false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReporterLimitCountsUtf8Bytes(bool multibyte)
    {
        var reports = new JsonArray(Report(100, new string(multibyte ? '\u00e9' : 'a', multibyte ? 32 : 64)));
        var root = Snapshot(reports);
        AssertAccepted(root, true);
        reports[0]!["reporter"] = new string(multibyte ? '\u00e9' : 'a', multibyte ? 33 : 65);
        AssertAccepted(root, false);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("reporter-order")]
    [InlineData("item-order")]
    public void ReportsMustBeUniqueAndOrderedByItemThenReporter(string change)
    {
        var reports = new JsonArray(Report(100, "A"), Report(100, "B"), Report(101, "A"));
        var root = Snapshot(reports);
        AssertAccepted(root, true);
        if (change == "duplicate")
        {
            reports[1]!["reporter"] = "A";
            reports[1]!["reportedAtUtc"] = "2026-09-05T12:00:00.0000000Z";
        }
        else if (change == "reporter-order") reports[0]!["reporter"] = "C";
        else reports[2]!["itemId"] = 99;
        AssertAccepted(root, false);
    }
}
