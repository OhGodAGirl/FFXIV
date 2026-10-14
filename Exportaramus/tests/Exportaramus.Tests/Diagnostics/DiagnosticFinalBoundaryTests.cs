using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.Features.Diagnostics;
using Exportaramus.Features.Main;

namespace Exportaramus.Tests.Diagnostics;

public sealed class DiagnosticFinalBoundaryTests
{
    private static DiagnosticReport Report => new() { GeneratedAtUtc = new(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc) };

    [Theory]
    [InlineData("Ready", true)]
    [InlineData("Partial", false)]
    public void ACorrectFingerprintDoesNotAuthorizeContradictorySectionStatus(string status, bool incomplete)
    {
        var wire = Wire(root =>
        {
            root["reportStatus"] = status;
            root["incompleteSections"] = incomplete ? new JsonArray("storage") : new JsonArray();
        });

        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.ValidateWireJson(wire));
    }

    [Theory]
    [InlineData("arrayText")]
    [InlineData("dynamicProperty")]
    public void ACorrectFingerprintDoesNotAuthorizeProhibitedWireData(string kind)
    {
        var wire = Wire(root =>
        {
            if (kind == "dynamicProperty") root["runtime"]!["dependencies"]!["contentId"] = "redacted";
            else root["recentFailures"] = JsonSerializer.SerializeToNode(new[]
            {
                DiagnosticRedactionPolicy.Failure(Report.GeneratedAtUtc, "Storage", "Write", "WriteFailed", null)
                    with { Frames = [new("alice@example.com", "Storage", "Write", "Source.cs", 1)] }
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        });

        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.ValidateWireJson(wire));
    }

    [Fact]
    public void WireAdmissionRejectsMalformedUtf16BeforeJsonTranscodingCanReplaceIt()
    {
        var prepared = DiagnosticReportSerializer.Prepare(Report);
        var malformed = prepared.Json.Replace("\"pluginVersion\":\"0.1.0.0\"", "\"pluginVersion\":\"bad" + '\ud800' + "\"", StringComparison.Ordinal);
        Assert.NotEqual(prepared.Json, malformed);

        Assert.Throws<EncoderFallbackException>(() => DiagnosticReportSerializer.ValidateWireJson(malformed));
    }

    [Fact]
    public void UnobservedDefaultGeometryDeclaresItsWideLayoutAndLeavesUnknownCollectionDecorationsEmpty()
    {
        var input = MainSnapshotPresenter.DiagnosticInput(null, "0.1.0.0", "10.0.11", Guid.Empty, false, false, 0, new(0, 0, 0), new(), Report.GeneratedAtUtc);
        var prepared = DiagnosticReportSerializer.Prepare(DiagnosticReportPreparer.Prepare(input, Report.GeneratedAtUtc));
        using var json = JsonDocument.Parse(prepared.Json);
        Assert.Contains("ui", prepared.Model.IncompleteSections);
        Assert.True(json.RootElement.GetProperty("ui").GetProperty("wideActions").GetBoolean());
        Assert.Equal(740, json.RootElement.GetProperty("ui").GetProperty("contentWidth").GetDouble());
        Assert.Equal(3, json.RootElement.GetProperty("ui").GetProperty("collectionColumnCount").GetInt32());
        var row = MainPresenter.Row(("mounts", "Mounts", "Mount"), null);
        Assert.Equal("—", row.Count);
        Assert.Equal("", row.Marker);
        Assert.Equal("", row.Delta);
    }

    private static string Wire(Action<JsonObject> change)
    {
        var root = JsonNode.Parse(DiagnosticReportSerializer.Prepare(Report).Json)!.AsObject();
        change(root);
        using var document = JsonDocument.Parse(root.ToJsonString());
        root["fingerprintSha256"] = Convert.ToHexStringLower(SHA256.HashData(DiagnosticReportSerializer.Canonical(document.RootElement, true)));
        return root.ToJsonString();
    }

    [Fact]
    public async Task RepeatedCompletedDisposalLeavesTheFinalImmutableViewUntouched()
    {
        var sourceCalls = 0;
        var controller = new DiagnosticsController(_ => { sourceCalls++; return Task.FromResult(Report); }, new(), _ => { });
        await controller.DisposeAsync();
        var finalView = controller.View;

        await controller.DisposeAsync();
        controller.Dispatch(DiagnosticsIntent.GenerateReport);
        controller.Update();

        Assert.Same(finalView, controller.View);
        Assert.False(controller.View.Running);
        Assert.Equal(0, sourceCalls);
        Assert.Equal("NotStarted", controller.OwnedTask.State);
    }
}
