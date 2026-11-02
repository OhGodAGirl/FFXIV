using Dalamud.Plugin;

namespace BetterGlamorPlates.Bootstrap;

internal sealed partial class ConfigurationStore
{
    internal ConfigurationStore(IDalamudPluginInterface pluginInterface)
        : this(pluginInterface.GetPluginConfig, configuration => pluginInterface.SavePluginConfig(configuration))
    {
    }
}
