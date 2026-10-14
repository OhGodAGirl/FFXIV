using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticRuntimeMetadata(string DotnetRuntimeVersion, string OsVersion,
    ImmutableDictionary<string, string> Dependencies)
{
    public DiagnosticRuntime Apply(DiagnosticRuntime observation) => observation with
    {
        DotnetRuntimeVersion = DotnetRuntimeVersion,
        OsVersion = OsVersion,
        Dependencies = Dependencies,
    };
}
