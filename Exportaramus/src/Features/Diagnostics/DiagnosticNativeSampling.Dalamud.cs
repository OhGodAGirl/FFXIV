using Dalamud.Plugin.Services;
using NativeFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace Exportaramus.Features.Diagnostics;

internal static unsafe class DiagnosticNativeSampling
{
    internal static string ClientVersion(IFramework framework)
    {
        if (!framework.IsInFrameworkUpdateThread) return "Unavailable";
        var instance = NativeFramework.Instance();
        if (instance == null) return "Unavailable";
        var version = instance->GameVersion;
        return DiagnosticSamplingPolicy.ClientVersion(version[..Math.Min(version.Length, 64)]);
    }
}
