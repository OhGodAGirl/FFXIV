using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Exportaramus.Shared.Ui;

namespace Exportaramus.Features.Main;

internal static class SnapshotSummaryRenderer
{
    internal static void Draw(Vector2 pos, float width, int columns, MainViewState state, IFontHandle font)
    {
        var u = Drawing.Unit;
        var minimumCell = 0f;
        foreach (var item in state.Rows)
            minimumCell = Math.Max(minimumCell, ImGui.CalcTextSize(item.Label).X + ImGui.CalcTextSize(item.Count + item.Marker + item.Delta).X + 72 * u);
        while (columns > 1 && (width - (16 + (columns - 1) * 8) * u) / columns < minimumCell) columns--;
        var rowCount = (state.Rows.Length + columns - 1) / columns;
        var height = (36 + rowCount * 28) * u;
        Drawing.Panel(pos, new(width, height));
        ImGui.SetCursorScreenPos(pos + Drawing.Offset(10, 8)); Drawing.Heading("SNAPSHOT SUMMARY", font);
        ImGui.SameLine(); ImGui.TextUnformatted(state.CollectionCountText);
        var columnWidth = (width - (16 + (columns - 1) * 8) * u) / columns;
        for (var column = 0; column < columns; column++)
        {
            var start = pos + new Vector2(8 * u + column * (columnWidth + 8 * u), 30 * u);
            Drawing.Panel(start, new(columnWidth, rowCount * 28 * u));
            for (var row = 0; row < rowCount; row++)
            {
                var index = row * columns + column;
                if (index >= state.Rows.Length) continue;
                var item = state.Rows[index]; var cell = start + new Vector2(0, row * 28 * u); var draw = ImGui.GetWindowDrawList();
                if (row % 2 == 1) draw.AddRectFilled(cell + Vector2.One, cell + new Vector2(columnWidth - 1, 27 * u), Drawing.Color(VisualTokens.Alternate));
                draw.AddLine(cell + Drawing.Offset(2, 27), cell + new Vector2(columnWidth - 2 * u, 27 * u), Drawing.Color(new(.29f, .278f, .235f, .7f)));
                UiIcons.Draw(item.Icon, cell + Drawing.Offset(10, 5), 18 * u);
                draw.AddText(cell + Drawing.Offset(38, 5), Drawing.Color(VisualTokens.Primary), item.Label);
                var deltaWidth = item.Delta.Length == 0 ? 0 : ImGui.CalcTextSize(item.Delta).X + 8 * u;
                var countWidth = ImGui.CalcTextSize(item.Count).X;
                var countPos = cell + new Vector2(columnWidth - 10 * u - deltaWidth - countWidth, 5 * u);
                draw.AddText(countPos, Drawing.Color(item.CountTone == UiTone.Muted ? VisualTokens.Muted : VisualTokens.Count), item.Count);
                if (item.Marker.Length > 0) draw.AddText(countPos - new Vector2(ImGui.CalcTextSize(item.Marker).X + 4 * u, 0), Drawing.Color(VisualTokens.Warning), item.Marker);
                ImGui.SetCursorScreenPos(cell);
                ImGui.InvisibleButton("##collection-" + item.Key, new(columnWidth, 28 * u));
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(item.Detail);
                if (item.Delta.Length > 0) draw.AddText(cell + new Vector2(columnWidth - deltaWidth - 2 * u, 5 * u), Drawing.Color(VisualTokens.Tone(item.DeltaTone)), item.Delta);
            }
        }
        ImGui.SetCursorScreenPos(pos + new Vector2(0, height)); ImGui.Dummy(new(width, 1));
    }
}
