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
        // Called only from Framework.Update, never from construction or draw.
        // A failed read retains the last prepared palette; first use defaults to Dark.
        policy.Update(seconds);
    }
}
