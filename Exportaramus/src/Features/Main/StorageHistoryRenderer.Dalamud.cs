using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Exportaramus.Shared.Ui;

namespace Exportaramus.Features.Main;

internal static class StorageHistoryRenderer
{
    private static bool Stack(float width, MainViewState state) => width < Math.Max(ImGui.CalcTextSize(state.HistoryCountText).X, ImGui.CalcTextSize(state.LatestSnapshotText).X) + Drawing.ButtonWidth("Sync Now") + 76 * Drawing.Unit;
    internal static float Height(float width, MainViewState state) => (Stack(width, state) ? 126 : 88) * Drawing.Unit;
    internal static void Draw(Vector2 pos, Vector2 size, MainViewState state, IFontHandle font, Action<MainIntent> dispatch)
    {
        var u = Drawing.Unit;
        Drawing.Panel(pos, size); ImGui.SetCursorScreenPos(pos + Drawing.Offset(12, 10)); Drawing.Heading("STORAGE & HISTORY", font);
        UiIcons.Draw("Storage", pos + Drawing.Offset(16, 44), 27 * u);
        ImGui.SetCursorScreenPos(pos + Drawing.Offset(52, 40)); ImGui.TextUnformatted(state.HistoryCountText);
        ImGui.SetCursorScreenPos(pos + Drawing.Offset(52, 62)); ImGui.TextColored(VisualTokens.Secondary, state.LatestSnapshotText);
        var buttonWidth = Drawing.ButtonWidth("Sync Now");
        ImGui.SetCursorScreenPos(pos + new Vector2(size.X - buttonWidth - 12 * u, (Stack(size.X, state) ? 86 : 42) * u));
        if (Drawing.Button("Sync Now", "##SyncNow", new(buttonWidth, 30 * u), state.CanSync && !state.RetainersActive, state.ReduceMotion)) dispatch(MainIntent.SyncNow);
        Drawing.Tooltip(TooltipId.SyncNow);
    }
}
