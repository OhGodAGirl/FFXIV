using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticRuntime
{
    public string DalamudApiVersion { get; init; } = "15";
    public string DotnetRuntimeVersion { get; init; } = "10.0.0";
    public string OsVersion { get; init; } = "Windows";
    public string FfxivClientVersion { get; init; } = "Unavailable";
    public string GameDataVersion { get; init; } = "Unavailable";
    public string ClientLanguage { get; init; } = "English";
    public ImmutableDictionary<string, string> Dependencies { get; init; } = ImmutableDictionary<string, string>.Empty;
}
