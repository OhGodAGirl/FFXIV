using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticReportInput(DiagnosticBuild Build, DiagnosticRuntime Runtime,
    DiagnosticLifecycle Lifecycle, DiagnosticOperations Operations, DiagnosticPipeline Pipeline,
    ImmutableArray<DiagnosticSource> Sources, DiagnosticStorage Storage, DiagnosticIpc Ipc,
    DiagnosticUi Ui, ImmutableArray<string> IncompleteSections);
