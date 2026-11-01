using Dalamud.Game.Config;
using Dalamud.Plugin.Services;

namespace Exportaramus.Shared.Ui;

internal sealed class GameThemeObserver(IGameConfig config)
{
    private readonly ThemeObservationPolicy policy = new(
        () => config.TryGet(SystemConfigOption.ColorThemeType, out uint theme) ? theme : null,
        theme => VisualTokens.ApplyTheme(theme));

    internal void Update(double seconds)
    {
        // Call from Framework.Update only. Keep the last palette on failure; default to Dark on first use.
        policy.Update(seconds);
    }
}
