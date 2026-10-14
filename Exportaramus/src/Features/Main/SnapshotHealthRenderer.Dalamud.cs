using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Exportaramus.Shared.Ui;

namespace Exportaramus.Features.Main;

internal static class SnapshotHealthRenderer
{
    private static bool Stack(float width, MainViewState state) => width < ImGui.CalcTextSize(state.HealthHeadline).X + Drawing.ButtonWidth("Copy JSON", true) + 80 * Drawing.Unit;
    internal static float Height(float width, MainViewState state)
    {
        return (Stack(width, state) ? 108 : 80) * Drawing.Unit;
    }
    internal static void Draw(Vector2 pos, Vector2 size, MainViewState state, IFontHandle font, Action<MainIntent> dispatch)
    {
        var u = Drawing.Unit;
        Drawing.Panel(pos, size); ImGui.SetCursorScreenPos(pos + Drawing.Offset(12, 10)); Drawing.Heading("SNAPSHOT HEALTH", font);
        var draw = ImGui.GetWindowDrawList(); var color = Drawing.Color(VisualTokens.Tone(state.HealthTone));
        var glyph = pos + Drawing.Offset(30, 50); draw.AddCircle(glyph, 17 * u, color, 24, 2 * u);
        if (state.HealthTone == UiTone.Success)
        {
            draw.AddLine(glyph + Drawing.Offset(-8, 0), glyph + Drawing.Offset(-2, 7), color, 2 * u);
            draw.AddLine(glyph + Drawing.Offset(-2, 7), glyph + Drawing.Offset(10, -9), color, 2 * u);
        }
        else
        {
            draw.AddLine(glyph + Drawing.Offset(0, -9), glyph + Drawing.Offset(0, 3), color, 2 * u);
            draw.AddCircleFilled(glyph + Drawing.Offset(0, 9), 1.5f * u, color);
        }
        ImGui.SetCursorScreenPos(pos + Drawing.Offset(60, 40)); ImGui.TextColored(VisualTokens.Tone(state.HealthTone), state.HealthHeadline);
        if (ImGui.IsItemHovered() || ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(glyph - new Vector2(19 * u), glyph + new Vector2(19 * u)))
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(420 * u);
            ImGui.TextUnformatted(state.HealthTooltip);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
        var stack = Stack(size.X, state); var buttonWidth = Drawing.ButtonWidth("Copy JSON", true);
        ImGui.SetCursorScreenPos(pos + new Vector2(size.X - buttonWidth - 12 * u, (stack ? 66 : 34) * u));
        if (Drawing.Button("Copy JSON", "##CopyJson", new(buttonWidth, 30 * u), state.CanCopy, state.ReduceMotion, "Copy")) dispatch(MainIntent.CopyJson);
        Drawing.Tooltip(TooltipId.CopyJson);
    }
}
