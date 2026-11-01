using System.Text.Json.Nodes;
using Exportaramus.Features.Diagnostics;

namespace Exportaramus.Tests.Diagnostics;

public sealed class DiagnosticReviewBoundaryTests
{
    private static DiagnosticReport Report => new() { GeneratedAtUtc = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc) };

    [Fact]
    public void FingerprintIsAssignedOnlyToThePreparedReport()
    {
        var report = Report;
        Assert.Empty(report.FingerprintSha256);
        var prepared = DiagnosticReportSerializer.Prepare(report);
        Assert.Equal(64, prepared.Model.FingerprintSha256.Length);
        DiagnosticReportSerializer.ValidateWireJson(prepared.Json);
        Assert.Empty(report.FingerprintSha256);
    }

    [Fact]
    public void UnsafeWireTextReportsTheRedactionFailure()
    {
        var prepared = DiagnosticReportSerializer.Prepare(Report);
        DiagnosticReportSerializer.ValidateWireJson(prepared.Json);
        var wire = JsonNode.Parse(prepared.Json)!;
        wire["build"]!["pluginVersion"] = "alice@example.com";
        var error = Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.ValidateWireJson(wire.ToJsonString()));
        Assert.Equal("DiagnosticProhibitedText", error.Message);
    }

    [Fact]
    public void UnsafeFailureSymbolReportsTheRedactionFailure()
    {
        var failure = DiagnosticRedactionPolicy.Failure(Report.GeneratedAtUtc, "Storage", "Read", "ReadFailed", "IOException");
        DiagnosticReportSerializer.Prepare(Report with { RecentFailures = [failure] });
        var error = Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.Prepare(Report with { RecentFailures = [failure with { Code = "bad/path" }] }));
        Assert.Equal("DiagnosticProhibitedText", error.Message);
    }
}
