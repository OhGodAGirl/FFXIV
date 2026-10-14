using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticReport
{
    public string Format { get; init; } = "exportaramus-diagnostics";
    public string SchemaVersion { get; init; } = "1.0";
    public DateTime GeneratedAtUtc { get; init; }
    public string ReportStatus { get; init; } = "Ready";
    public string FingerprintSha256 { get; init; } = "";
    public ImmutableArray<string> IncompleteSections { get; init; } = [];
    public DiagnosticBuild Build { get; init; } = new();
    public DiagnosticRuntime Runtime { get; init; } = new();
    public DiagnosticLifecycle Lifecycle { get; init; } = new();
    public DiagnosticOperations Operations { get; init; } = new();
    public DiagnosticPipeline CollectionPipeline { get; init; } = new();
    public ImmutableArray<DiagnosticSource> SourceHealth { get; init; } = [];
    public DiagnosticStorage Storage { get; init; } = new();
    public DiagnosticIpc Ipc { get; init; } = new();
    public ImmutableDictionary<string, DiagnosticMetric> Performance { get; init; } = ImmutableDictionary<string, DiagnosticMetric>.Empty;
    public DiagnosticUi Ui { get; init; } = new();
    public ImmutableArray<DiagnosticFailure> RecentFailures { get; init; } = [];
    public DiagnosticRedaction Redaction { get; init; } = new();
}
