using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Exportaramus.Shared.Ui;

namespace Exportaramus.Features.Main;

internal static class OperationsRenderer
{
    private static bool StackInformation(float width) => width < ImGui.CalcTextSize("Disable nonessential animation").X + ImGui.CalcTextSize("Last Refresh").X + 100 * Drawing.Unit;
    private static bool StackButtons(float width, MainViewState state) => width < 2 * Math.Max(Drawing.ButtonWidth("Cancel Retainers", true), Drawing.ButtonWidth(state.Guided.Active ? "Cancel Capture" : "Dresser & Armoire", !state.Guided.Active)) + 32 * Drawing.Unit;
    internal static float Height(float width, MainViewState state) => (StackInformation(width) ? 232 : 188) * Drawing.Unit + (StackButtons(width, state) ? 44 * Drawing.Unit : 0);
    internal static void Draw(Vector2 pos, Vector2 size, MainViewState state, IFontHandle font, Action<MainIntent> dispatch, bool returnGuidedFocus)
    {
        var u = Drawing.Unit;
        Drawing.Panel(pos, size); ImGui.SetCursorScreenPos(pos + Drawing.Offset(12, 10)); Drawing.Heading("GUIDED CAPTURE", font);
        UiIcons.Draw("Clock", pos + Drawing.Offset(16, 46), 24 * u);
        ImGui.SetCursorScreenPos(pos + Drawing.Offset(52, 42)); ImGui.TextUnformatted("Last Refresh");
        ImGui.SetCursorScreenPos(pos + Drawing.Offset(52, 64)); ImGui.TextColored(VisualTokens.Secondary, state.LastRefresh);
        var second = StackInformation(size.X) ? Drawing.Offset(16, 90) : new Vector2(size.X * .48f, 40 * u);
        ImGui.SetCursorScreenPos(pos + second);
        var reduceMotion = state.ReduceMotion;
        if (ImGui.Checkbox("Reduce Motion##ReduceMotion", ref reduceMotion)) dispatch(MainIntent.ToggleReduceMotion);
        Drawing.Tooltip(TooltipId.ReduceMotion);
        ImGui.SetCursorScreenPos(pos + second + Drawing.Offset(0, 26)); ImGui.TextColored(VisualTokens.Muted, "Disable nonessential animation");
        var stackButtons = StackButtons(size.X, state);
        var actionY = size.Y - (stackButtons ? 92 : 48) * u;
        ImGui.GetWindowDrawList().AddLine(pos + new Vector2(12 * u, actionY - 10 * u), pos + new Vector2(size.X - 12 * u, actionY - 10 * u), Drawing.Color(VisualTokens.Border));
        var buttonWidth = stackButtons ? size.X - 24 * u : (size.X - 32 * u) / 2;
        ImGui.SetCursorScreenPos(pos + new Vector2(12 * u, actionY));
        if (Drawing.Button(state.RetainersActive ? "Cancel Retainers" : "Retainers", "##Retainers", new(buttonWidth, 36 * u), state.RetainersActive || state.CanGuide && !state.Guided.Active, state.ReduceMotion, "Storage")) dispatch(MainIntent.Retainers);

        ImGui.SetCursorScreenPos(pos + new Vector2(stackButtons ? 12 * u : 20 * u + buttonWidth, actionY + (stackButtons ? 44 * u : 0)));
        if (returnGuidedFocus) ImGui.SetKeyboardFocusHere();
        if (Drawing.Button(state.Guided.Active ? "Cancel Capture" : "Dresser & Armoire", "##GuidedCapture", new(buttonWidth, 36 * u), state.Guided.Active || state.CanGuide && !state.RetainersActive, state.ReduceMotion, state.Guided.Active ? "" : "Capture", state.Guided.Active))
            dispatch(state.Guided.Active ? MainIntent.CancelGuided : MainIntent.GuidedCapture);
        Drawing.Tooltip(TooltipId.GuidedCapture);
    }
}
