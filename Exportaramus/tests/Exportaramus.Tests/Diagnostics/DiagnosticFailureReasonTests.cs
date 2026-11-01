using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Exportaramus.Features.Diagnostics;

namespace Exportaramus.Tests.Diagnostics;

public sealed class DiagnosticFailureReasonTests
{
    private static DiagnosticReport Valid => new() { GeneratedAtUtc = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc) };

    [Fact]
    public async Task MissingDiagnosticMetadataExplainsWhyReadingFailed()
    {
        var source = new DiagnosticReportSource(new());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadAsync(default));
        Assert.Equal("Verified diagnostic metadata is unavailable.", error.Message);
        source.Set(Valid);
        Assert.Equal("Ready", (await source.ReadAsync(default)).ReportStatus);
    }

    [Theory]
    [InlineData("section", "DiagnosticSectionStatusInvalid")]
    [InlineData("capacity", "DiagnosticCapacityExceeded")]
    [InlineData("text", "DiagnosticProhibitedText")]
    [InlineData("path", "DiagnosticProhibitedPath")]
    [InlineData("field", "DiagnosticProhibitedField")]
    public void RejectedReportsRetainTheFailureReason(string kind, string expected)
    {
        var valid = Valid;
        DiagnosticReportSerializer.ValidateWireJson(DiagnosticReportSerializer.Prepare(valid).Json);
        var failure = DiagnosticRedactionPolicy.Failure(valid.GeneratedAtUtc, "Storage", "Read", "ReadFailed", "IOException");
        var report = kind switch
        {
            "section" => valid with { IncompleteSections = ["Storage"] },
            "capacity" => valid with { RecentFailures = Enumerable.Repeat(failure, 21).ToImmutableArray() },
            "text" => valid with { Build = valid.Build with { PluginVersion = "alice@example.com" } },
            "path" => valid with { RecentFailures = [failure with { Frames = [new("Assembly", "Type", "Method", "folder/File.cs", 1)] }] },
            "field" => valid with { Performance = valid.Performance.Add("contentId", new(1, new(0, 0, 0), new(0, 0, 0))) },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var error = Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.Prepare(report));
        Assert.Equal(expected, error.Message);
    }

    [Fact]
    public void InconsistentWireStatusRetainsTheFailureReason()
    {
        var prepared = DiagnosticReportSerializer.Prepare(Valid);
        DiagnosticReportSerializer.ValidateWireJson(prepared.Json);
        var wire = JsonNode.Parse(prepared.Json)!;
        wire["reportStatus"] = "Partial";
        var error = Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.ValidateWireJson(wire.ToJsonString()));
        Assert.Equal("DiagnosticSectionStatusInvalid", error.Message);
    }
}
