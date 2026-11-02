using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EZDelivery.Features.Interface;

internal static class PluginTheme
{
    private static uint gameTheme;
    internal static uint CurrentTheme => gameTheme;
    internal static void SetGameTheme(uint theme) => gameTheme = theme <= 7 ? theme : 0;
    internal static Vector4 Themed(Vector4 original) => ThemePalette.Transform(gameTheme, original);
    internal static Vector4 WindowBackground => ThemeColors.Resolve(gameTheme, ThemeColorRole.WindowBg);
    internal static Vector4 PanelBackground => ThemeColors.Resolve(gameTheme, ThemeColorRole.ChildBg);
    internal static Vector4 FrameBackground => ThemeColors.Resolve(gameTheme, ThemeColorRole.FrameBg);
    internal static Vector4 Gold => ThemeColors.Resolve(gameTheme, ThemeColorRole.CheckMark);
    internal static Vector4 GoldMuted => ThemeColors.Resolve(gameTheme, ThemeColorRole.Border);
    internal static Vector4 Text => ThemeColors.Resolve(gameTheme, ThemeColorRole.Text);
    internal static Vector4 TextMuted => ThemeColors.Resolve(gameTheme, ThemeColorRole.TextDisabled);

    private const int StyleColorCount = 31;
    private const int StyleVariableCount = 8;

    internal static void Push()
    {
        ImGui.SetNextWindowBgAlpha(WindowBackground.W);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 3f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 3f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 10f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(7f, 4f));
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(6f, 4f));

        ImGui.PushStyleColor(ImGuiCol.Text, ThemeColors.Resolve(gameTheme, ThemeColorRole.Text));
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, ThemeColors.Resolve(gameTheme, ThemeColorRole.TextDisabled));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, ThemeColors.Resolve(gameTheme, ThemeColorRole.WindowBg));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, ThemeColors.Resolve(gameTheme, ThemeColorRole.ChildBg));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, ThemeColors.Resolve(gameTheme, ThemeColorRole.PopupBg));
        ImGui.PushStyleColor(ImGuiCol.Border, ThemeColors.Resolve(gameTheme, ThemeColorRole.Border));
        ImGui.PushStyleColor(ImGuiCol.BorderShadow, ThemeColors.Resolve(gameTheme, ThemeColorRole.BorderShadow));
        ImGui.PushStyleColor(ImGuiCol.Separator, ThemeColors.Resolve(gameTheme, ThemeColorRole.Separator));
        ImGui.PushStyleColor(ImGuiCol.TitleBg, ThemeColors.Resolve(gameTheme, ThemeColorRole.TitleBg));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, ThemeColors.Resolve(gameTheme, ThemeColorRole.TitleBgActive));
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, ThemeColors.Resolve(gameTheme, ThemeColorRole.TitleBgCollapsed));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, ThemeColors.Resolve(gameTheme, ThemeColorRole.FrameBg));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, ThemeColors.Resolve(gameTheme, ThemeColorRole.FrameBgHovered));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, ThemeColors.Resolve(gameTheme, ThemeColorRole.FrameBgActive));
        ImGui.PushStyleColor(ImGuiCol.Button, ThemeColors.Resolve(gameTheme, ThemeColorRole.Button));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ThemeColors.Resolve(gameTheme, ThemeColorRole.ButtonHovered));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ThemeColors.Resolve(gameTheme, ThemeColorRole.ButtonActive));
        ImGui.PushStyleColor(ImGuiCol.CheckMark, ThemeColors.Resolve(gameTheme, ThemeColorRole.CheckMark));
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, ThemeColors.Resolve(gameTheme, ThemeColorRole.TableHeaderBg));
        ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, ThemeColors.Resolve(gameTheme, ThemeColorRole.TableBorderStrong));
        ImGui.PushStyleColor(ImGuiCol.TableBorderLight, ThemeColors.Resolve(gameTheme, ThemeColorRole.TableBorderLight));
        ImGui.PushStyleColor(ImGuiCol.TableRowBg, ThemeColors.Resolve(gameTheme, ThemeColorRole.TableRowBg));
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, ThemeColors.Resolve(gameTheme, ThemeColorRole.TableRowBgAlt));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, ThemeColors.Resolve(gameTheme, ThemeColorRole.ScrollbarBg));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, ThemeColors.Resolve(gameTheme, ThemeColorRole.ScrollbarGrab));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, ThemeColors.Resolve(gameTheme, ThemeColorRole.ScrollbarGrabHovered));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, ThemeColors.Resolve(gameTheme, ThemeColorRole.ScrollbarGrabActive));
        ImGui.PushStyleColor(ImGuiCol.Header, ThemeColors.Resolve(gameTheme, ThemeColorRole.Header));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, ThemeColors.Resolve(gameTheme, ThemeColorRole.HeaderHovered));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, ThemeColors.Resolve(gameTheme, ThemeColorRole.HeaderActive));
        ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, ThemeColors.Resolve(gameTheme, ThemeColorRole.TextSelectedBg));
    }

    internal static void Pop()
    {
        ImGui.PopStyleColor(StyleColorCount);
        ImGui.PopStyleVar(StyleVariableCount);
    }

}
