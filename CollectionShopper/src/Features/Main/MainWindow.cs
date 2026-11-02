using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace CollectionShopper;

internal sealed class MainWindow : ShopperWindow
{
    internal MainWindow(
        Plugin plugin,
        IDalamudPluginInterface pluginInterface,
        FfxivCollectClient client,
        ShoppingController shoppingController,
        IFramework framework,
        IPlayerState playerState,
        IDataManager dataManager,
        ITextureProvider textureProvider,
        IPluginLog log,
        CancellationToken cancellationToken)
        : base(
            plugin,
            pluginInterface,
            client,
            shoppingController,
            framework,
            playerState,
            dataManager,
            textureProvider,
            log,
            cancellationToken)
    {
    }

}
