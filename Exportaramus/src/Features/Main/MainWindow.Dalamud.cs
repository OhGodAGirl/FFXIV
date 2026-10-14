using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using Exportaramus.Shared.Ui;
using Exportaramus.Features.Diagnostics;
using Dalamud.Interface.Utility;

namespace Exportaramus.Features.Main;

public sealed class MainWindow : Window
{
    private readonly Func<MainViewState> view;
    private readonly Action<MainIntent> dispatch;
    private readonly IFontHandle titleFont;
    private readonly IFontHandle headingFont;
    private WindowStyle style;
    private bool promptWasOpen;
    public DiagnosticUiObservation? DiagnosticObservation { get; private set; }
    public MainWindow(Func<MainViewState> view, Action<MainIntent> dispatch, IFontHandle titleFont, IFontHandle headingFont)
        : base("Exportaramus##Exportaramus.Main", ImGuiWindowFlags.NoTitleBar)
    {
        this.view = view; this.dispatch = dispatch; this.titleFont = titleFont; this.headingFont = headingFont;
        Size = new Vector2(760, view().HasSnapshot ? 560 : 320);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new(680, 360), MaximumSize = new(float.MaxValue) };
    }
    public override void PreDraw() { style = new(); ImGui.SetNextWindowBgAlpha(.995f); }
    public override void PostDraw() => style.Dispose();
    public override void OnOpen() => DiagnosticObservation = null;
    public override void OnClose() => DiagnosticObservation = null;
    public override void Draw()
    {
        var state = view(); var origin = ImGui.GetCursorScreenPos(); var width = ImGui.GetContentRegionAvail().X;
        var unit = Drawing.Unit;
        var layout = LayoutPolicy.At(width / unit);
        var returnGuidedFocus = !state.Guided.Paused && promptWasOpen;
        Drawing.Frame();
        var top = origin + new Vector2(0, MainHeaderRenderer.Draw(origin, width, state, titleFont, dispatch));
        if (layout.Wide)
        {
            var leftWidth = layout.OperationsWidth * unit; var rightWidth = layout.RightWidth * unit;
            var storageHeight = StorageHistoryRenderer.Height(rightWidth, state);
            var healthHeight = SnapshotHealthRenderer.Height(rightWidth, state);
            var height = Math.Max(OperationsRenderer.Height(leftWidth, state), storageHeight + healthHeight + 8 * unit);
            OperationsRenderer.Draw(top, new(leftWidth, height), state, headingFont, dispatch, returnGuidedFocus);
            var right = top + new Vector2(leftWidth + 8 * unit, 0);
            StorageHistoryRenderer.Draw(right, new(rightWidth, storageHeight), state, headingFont, dispatch);
            SnapshotHealthRenderer.Draw(right + new Vector2(0, storageHeight + 8 * unit), new(rightWidth, height - storageHeight - 8 * unit), state, headingFont, dispatch);
            top.Y += height + 8 * unit;
        }
        else
        {
            var height = OperationsRenderer.Height(width, state);
            OperationsRenderer.Draw(top, new(width, height), state, headingFont, dispatch, returnGuidedFocus); top.Y += height + 8 * unit;
            height = StorageHistoryRenderer.Height(width, state);
            StorageHistoryRenderer.Draw(top, new(width, height), state, headingFont, dispatch); top.Y += height + 8 * unit;
            height = SnapshotHealthRenderer.Height(width, state);
            SnapshotHealthRenderer.Draw(top, new(width, height), state, headingFont, dispatch); top.Y += height + 8 * unit;
        }
        if (state.Message.Length > 0)
        {
            ImGui.SetCursorScreenPos(top); ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            ImGui.TextColored(VisualTokens.Tone(state.StateTone), state.Message); ImGui.PopTextWrapPos();
            top.Y = ImGui.GetCursorScreenPos().Y + 8;
        }
        if (state.Guided.Active && !state.Guided.Paused)
        {
            Drawing.Wrapped(top, width, state.Guided.Prompt, VisualTokens.Primary);
            top.Y += Drawing.WrappedHeight(state.Guided.Prompt, width) + 12 * unit;
        }
        if (state.Guided.Paused)
        {
            var height = Drawing.WrappedHeight(state.Guided.Prompt, width - 24 * unit) + 90 * unit;
            Drawing.Panel(top, new(width, height));
            ImGui.SetCursorScreenPos(top + Drawing.Offset(12, 10)); Drawing.Heading("GUIDED CAPTURE", headingFont);
            Drawing.Wrapped(top + Drawing.Offset(12, 36), width - 24 * unit, state.Guided.Prompt, VisualTokens.Primary);
            ImGui.SetCursorScreenPos(top + new Vector2(12 * unit, height - 42 * unit));
            if (!promptWasOpen) ImGui.SetKeyboardFocusHere();
            if (Drawing.Button("Continue", "##GuidedContinue", new(120 * unit, 32 * unit), true, state.ReduceMotion)) dispatch(MainIntent.ContinueGuided);
            Drawing.Tooltip(TooltipId.Continue);
            if (state.Guided.CanSavePartial)
            {
                ImGui.SameLine();
                if (Drawing.Button("Save Partial", "##GuidedSavePartial", new(120 * unit, 32 * unit), true, state.ReduceMotion)) dispatch(MainIntent.SavePartial);
                Drawing.Tooltip(TooltipId.SavePartial);
            }
            ImGui.SameLine();
            if (Drawing.Button("Cancel", "##GuidedCancel", new(100 * unit, 32 * unit), true, state.ReduceMotion, cancel: true)) dispatch(MainIntent.CancelGuided);
            Drawing.Tooltip(TooltipId.Cancel);
            top.Y += height + 8 * unit;
        }
        promptWasOpen = state.Guided.Paused;
        if (state.RetainersMessage.Length > 0)
        {
            Drawing.Wrapped(top, width, state.RetainersMessage, VisualTokens.Primary);
            top.Y += Drawing.WrappedHeight(state.RetainersMessage, width) + 12 * unit;
        }
        if (state.HasSnapshot) SnapshotSummaryRenderer.Draw(top, width, layout.CollectionColumns, state, headingFont);
        else { ImGui.SetCursorScreenPos(top); ImGui.Dummy(new(width, 1)); }
        var position = ImGui.GetWindowPos(); var size = ImGui.GetWindowSize();
        DiagnosticObservation = new(position.X, position.Y, size.X, size.Y, width, ImGuiHelpers.GlobalScale,
            titleFont.Available && headingFont.Available, style.VariableCount, style.ColorCount,
            state.ReduceMotion, state.Revision);
    }
}
