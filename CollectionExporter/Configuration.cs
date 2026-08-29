using Dalamud.Configuration;

namespace CollectionExporter;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public ulong? FfxivCollectCharacterId { get; set; }
}
