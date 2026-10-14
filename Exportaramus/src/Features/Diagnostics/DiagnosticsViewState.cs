namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticsViewState(bool Running, string Status, string Error, PreparedDiagnosticReport? Report, bool ReduceMotion)
{
    public bool CanGenerate => !Running;
    public bool CanCopy => Report is not null;
}
