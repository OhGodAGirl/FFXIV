using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Exportaramus.Shared.Ui;

namespace Exportaramus.Features.Main;

internal static class MainHeaderRenderer
{
    internal static float Draw(Vector2 origin, float width, MainViewState state, IFontHandle titleFont, Action<MainIntent> dispatch)
    {
        var u = Drawing.Unit;
        ImGui.SetCursorScreenPos(origin); Drawing.Heading("Exportaramus", titleFont);
        var titleHeight = ImGui.GetItemRectSize().Y;
        ImGui.SetCursorScreenPos(origin + new Vector2(width - 28 * u, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        var closePressed = ImGui.Button("##Close Exportaramus", new Vector2(28 * u));
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        var iconSize = 18 * u;
        var iconOrigin = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax() - new Vector2(iconSize)) / 2;
        UiIcons.Draw("Close", iconOrigin, iconSize);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Close");
        if (closePressed) dispatch(MainIntent.Close);
        var y = Math.Max(36 * u, titleHeight + 6 * u);
        const string subtitle = "Snapshot and synchronize your FFXIV collection data with other plugins.";
        Drawing.Wrapped(origin + new Vector2(2 * u, y), width - 4 * u, subtitle, VisualTokens.Secondary);
        var subtitleHeight = ImGui.GetItemRectSize().Y;
        return y + subtitleHeight + 8 * u;
    }
}
