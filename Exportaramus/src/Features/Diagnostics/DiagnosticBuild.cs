using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticBuild
{
    public string PluginVersion { get; init; } = "0.1.0.0";
    public string BuildConfiguration { get; init; } = "Release";
    public string? SourceRevision { get; init; }
    public string AssemblyInformationalVersion { get; init; } = "0.1.0.0";
    public string? PackageSha256 { get; init; }
}
