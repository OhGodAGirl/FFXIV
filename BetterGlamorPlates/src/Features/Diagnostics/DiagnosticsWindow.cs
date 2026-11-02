using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace BetterGlamorPlates.Features.Diagnostics;

internal sealed class DiagnosticsWindow(Func<string> capture) : Window("Better Glamour Plates Diagnostics")
{
    private string report = "Select Copy Diagnostics to capture a support report.";

    public override void Draw()
    {
        ImGui.TextWrapped("Copy a support report with current availability and the last failed operation. This does not retry any action.");
        ImGui.TextWrapped("After a stopped load, review the native plate before saving. Include this report when reporting a problem.");
        if (ImGui.Button("Copy Diagnostics"))
        {
            report = capture();
            ImGui.SetClipboardText(report);
        }
        ImGui.Separator();
        ImGui.BeginChild("##report", new Vector2(0, 0), true, ImGuiWindowFlags.HorizontalScrollbar);
        ImGui.TextUnformatted(report);
        ImGui.EndChild();
    }
}
