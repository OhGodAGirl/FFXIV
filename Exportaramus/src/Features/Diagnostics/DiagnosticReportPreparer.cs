namespace Exportaramus.Features.Diagnostics;

public static class DiagnosticReportPreparer
{
    public static DiagnosticReport Prepare(DiagnosticReportInput input, DateTimeOffset now) => new()
    {
        GeneratedAtUtc = now.UtcDateTime,
        ReportStatus = input.IncompleteSections.IsEmpty ? "Ready" : "Partial",
        IncompleteSections = input.IncompleteSections,
        Build = input.Build,
        Runtime = input.Runtime,
        Lifecycle = input.Lifecycle,
        Operations = input.Operations,
        CollectionPipeline = input.Pipeline,
        SourceHealth = input.Sources,
        Storage = input.Storage,
        Ipc = input.Ipc,
        Ui = input.Ui,
    };
}
