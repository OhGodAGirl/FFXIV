using Dalamud.Configuration;
namespace Exportaramus.Bootstrap;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool ReduceMotion { get; set; }
}
