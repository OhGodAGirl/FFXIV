using Dalamud.Plugin.Services;

namespace CollectionShopper;

internal sealed partial class ShoppingController
{
    public ShoppingController(
        Plugin plugin,
        IFramework framework,
        IMarketBoard marketBoard,
        IGameGui gameGui,
        IPluginLog log)
        : this(new ShoppingContext(plugin, log),
            new ShoppingMarketBoardAdapter(framework, marketBoard, gameGui), () => DateTime.UtcNow)
    {
    }
}
