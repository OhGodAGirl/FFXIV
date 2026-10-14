using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using Exportaramus.Shared.Ui;

namespace Exportaramus.Features.Diagnostics;

public sealed class DiagnosticsWindow : Window
{
    private readonly Func<DiagnosticsViewState> view;
    private readonly Action<DiagnosticsIntent> dispatch;
    private readonly IFontHandle headingFont;
    private WindowStyle style;
    public DiagnosticsWindow(Func<DiagnosticsViewState> view, Action<DiagnosticsIntent> dispatch, IFontHandle headingFont)
        : base("Exportaramus Diagnostics##Exportaramus.Diagnostics")
    {
        this.view = view; this.dispatch = dispatch; this.headingFont = headingFont;
        Size = new Vector2(820, 680); SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new(620, 440), MaximumSize = new(float.MaxValue) };
    }
    public override void OnOpen() => dispatch(DiagnosticsIntent.GenerateReport);
    public override void OnClose() => dispatch(DiagnosticsIntent.Close);
    public override void PreDraw() { style = new(); ImGui.SetNextWindowBgAlpha(.995f); }
    public override void PostDraw() => style.Dispose();
    public override void Draw()
    {
        var state = view(); Drawing.Frame(); var width = ImGui.GetContentRegionAvail().X;
        if (Drawing.Button("Generate Report", "##GenerateReport", new(160, 32), state.CanGenerate, state.ReduceMotion)) dispatch(DiagnosticsIntent.GenerateReport);
        ImGui.SameLine();
        if (Drawing.Button("Copy Report", "##CopyReport", new(145, 32), state.CanCopy, state.ReduceMotion)) dispatch(DiagnosticsIntent.CopyReport);
        if (LayoutPolicy.WideDiagnosticActions(width)) ImGui.SameLine();
        if (Drawing.Button("Reset Measurements", "##ResetMeasurements", new(200, 32), true, state.ReduceMotion)) dispatch(DiagnosticsIntent.ResetMeasurements);
        ImGui.TextUnformatted(state.Status);
        if (state.Error.Length != 0) ImGui.TextWrapped(state.Error);
        if (state.Report is not { } report) return;
        ImGui.TextUnformatted(report.GeneratedText);
        ImGui.TextUnformatted(report.SizeText);
        ImGui.TextWrapped(report.FingerprintText);
        foreach (var section in report.Sections)
        {
            bool expanded;
            using (headingFont.Push()) expanded = ImGui.CollapsingHeader(section.Heading, section.InitiallyOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);
            if (!expanded) continue;
            if (ImGui.BeginTable(section.TableId, 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthStretch, .46f);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, .54f);
                foreach (var row in section.Rows)
                {
                    ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.TextWrapped(row.Label);
                    ImGui.TableNextColumn(); ImGui.TextWrapped(row.Value);
                }
                ImGui.EndTable();
            }
        }
    }
}
