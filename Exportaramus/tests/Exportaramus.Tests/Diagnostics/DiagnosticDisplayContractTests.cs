using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Exportaramus.Features.Diagnostics;

namespace Exportaramus.Tests.Diagnostics;

public sealed class DiagnosticDisplayContractTests
{
    private static DiagnosticReport Report => new() { GeneratedAtUtc = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc) };

    [Fact]
    public void MaximumSourceAndOwnedTaskCardinalitiesRemainReportableWithoutTruncation()
    {
        var input = Report with
        {
            SourceHealth = Enumerable.Range(0, 64).Select(index => new DiagnosticSource("Source" + index, "Supported", "Fresh", "Complete", "Observed", 0, 0, "None")).ToImmutableArray(),
            Lifecycle = new() { OwnedTasks = Enumerable.Range(0, 32).Select(index => new DiagnosticTask("Task" + index, "Running")).ToImmutableArray() },
        };

        var report = DiagnosticReportSerializer.Prepare(input);

        DiagnosticReportSerializer.ValidateWireJson(report.Json);
        Assert.Equal(input.SourceHealth, report.Model.SourceHealth); Assert.Equal(input.Lifecycle.OwnedTasks, report.Model.Lifecycle.OwnedTasks);
        Assert.Contains(new DiagnosticDisplayRow("sourceHealth[63].sourceKey", "Source63"), report.Sections[3].Rows);
        Assert.Contains(new DiagnosticDisplayRow("lifecycle.ownedTasks[31].name", "Task31"), report.Sections[1].Rows);
    }

    [Fact]
    public void CanonicalFingerprintExcludesRootMetadataButRetainsIdenticallyNamedNestedEvidence()
    {
        using var json = JsonDocument.Parse("{\"generatedAtUtc\":\"root\",\"fingerprintSha256\":\"root\",\"object\":{\"generatedAtUtc\":\"nested\",\"fingerprintSha256\":\"nested\"},\"array\":[{\"generatedAtUtc\":\"entry\",\"fingerprintSha256\":\"entry\"}]}");

        var canonical = Encoding.UTF8.GetString(DiagnosticReportSerializer.Canonical(json.RootElement, true));

        Assert.Equal("{\"array\":[{\"fingerprintSha256\":\"entry\",\"generatedAtUtc\":\"entry\"}],\"object\":{\"fingerprintSha256\":\"nested\",\"generatedAtUtc\":\"nested\"}}", canonical);
    }

    [Fact]
    public void AllowlistedFailureSymbolsRemainUsefulAfterRawTextIsDiscarded()
    {
        var failure = DiagnosticRedactionPolicy.Failure(Report.GeneratedAtUtc, "Storage", "Write", "WriteFailed", "IOException");

        Assert.Equal("Storage", failure.Component); Assert.Equal("Write", failure.Operation); Assert.Equal("WriteFailed", failure.Code);
        Assert.Equal("IOException", failure.ExceptionType); Assert.False(string.IsNullOrWhiteSpace(failure.Message));
        Assert.Empty(failure.Frames);
    }

    [Fact]
    public void ClientVersionAllowsTheFullAsciiDigitRange()
    {
        Assert.Equal("2099.09.09.0999.9999", DiagnosticSamplingPolicy.ClientVersion(Encoding.ASCII.GetBytes("2099.09.09.0999.9999\0")));
    }

    [Fact]
    public void ReportSectionsExposeCanonicalPathsValuesAndDistinctArrayIndexes()
    {
        var failure = DiagnosticRedactionPolicy.Failure(Report.GeneratedAtUtc, "Storage", "Write", "WriteFailed", "IOException");
        var input = Report with
        {
            Runtime = new() { Dependencies = ImmutableDictionary<string, string>.Empty.Add("Zeta", "2.0").Add("Alpha", "1.0") },
            RecentFailures = [failure, failure with { Component = "Capture" }],
        };

        var sections = DiagnosticReportSerializer.Prepare(input).Sections;

        Assert.Equal(new[] { "Build and Runtime", "Lifecycle and Operations", "Collection Pipeline", "Source Health", "Storage and IPC",
            "Performance", "UI", "Recent Failures", "Redaction" }, sections.Select(section => section.Heading));
        Assert.Equal(new[] { false, true, false, false, false, true, true, true, false }, sections.Select(section => section.InitiallyOpen));
        Assert.Contains(new DiagnosticDisplayRow("runtime.dependencies.Alpha", "1.0"), sections[0].Rows);
        Assert.Contains(new DiagnosticDisplayRow("runtime.dependencies.Zeta", "2.0"), sections[0].Rows);
        Assert.Equal(new[] { "runtime.dependencies.Alpha", "runtime.dependencies.Zeta" }, sections[0].Rows.Where(row => row.Label.StartsWith("runtime.dependencies.", StringComparison.Ordinal)).Select(row => row.Label));
        Assert.Contains(new DiagnosticDisplayRow("recentFailures[0].component", "Storage"), sections[7].Rows);
        Assert.Contains(new DiagnosticDisplayRow("recentFailures[1].component", "Capture"), sections[7].Rows);
        Assert.Contains(new DiagnosticDisplayRow("ui.collectionColumnCount", "3"), sections[6].Rows);
        Assert.Contains(new DiagnosticDisplayRow("redaction.schemaVersion", "1.0"), sections[8].Rows);
        Assert.Contains(sections[1].Rows, row => row.Label.StartsWith("lifecycle.", StringComparison.Ordinal));
        Assert.Contains(sections[1].Rows, row => row.Label.StartsWith("operations.", StringComparison.Ordinal));
        Assert.Contains(sections[2].Rows, row => row.Label.StartsWith("collectionPipeline.", StringComparison.Ordinal));
        Assert.Contains(sections[4].Rows, row => row.Label.StartsWith("storage.", StringComparison.Ordinal));
        Assert.Contains(sections[4].Rows, row => row.Label.StartsWith("ipc.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("contentId")]
    [InlineData("characterName")]
    [InlineData("displayName")]
    [InlineData("world")]
    [InlineData("itemId")]
    [InlineData("itemIds")]
    [InlineData("quantity")]
    [InlineData("retainerId")]
    [InlineData("inventory")]
    [InlineData("environment")]
    [InlineData("commandLine")]
    [InlineData("clipboardText")]
    [InlineData("password")]
    [InlineData("token")]
    [InlineData("secret")]
    public void ProhibitedIdentityAndEnvironmentFieldsCannotEnterDynamicDiagnosticMaps(string key)
    {
        var report = Report with { Runtime = new() { Dependencies = ImmutableDictionary<string, string>.Empty.Add(key, "1.0") } };

        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.Prepare(report));
    }
}
