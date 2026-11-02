using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace CollectionShopper;

internal sealed class DiagnosticsWindow : Window
{
    private readonly Func<DiagnosticSnapshot> capture;
    private DiagnosticSnapshot? snapshot;
    private bool stylesPushed;
    private string copyStatus = string.Empty;

    internal DiagnosticsWindow(Func<DiagnosticSnapshot> capture)
        : base("Collection Shopper Diagnostics##CollectionShopperDiagnostics")
    {
        this.capture = capture;
        Size = new Vector2(720f, 560f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560f, 400f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    internal void Open()
    {
        snapshot = capture();
        copyStatus = string.Empty;
        IsOpen = true;
    }

    public override void PreDraw()
    {
        PluginTheme.Push();
        stylesPushed = true;
    }

    public override void PostDraw()
    {
        if (!stylesPushed)
            return;

        PluginTheme.Pop();
        stylesPushed = false;
    }

    public override void Draw()
    {
        if (ImGui.Button("Run Checks"))
        {
            snapshot = capture();
            copyStatus = "Checks completed without changing game or plugin state.";
        }

        ImGui.SameLine();
        if (snapshot is null)
            ImGui.BeginDisabled();
        if (ImGui.Button("Copy Diagnostics") && snapshot is not null)
        {
            snapshot = capture();
            ImGui.SetClipboardText(DiagnosticReport.Build(snapshot));
            copyStatus = "Redacted report copied to the clipboard.";
        }
        if (snapshot is null)
            ImGui.EndDisabled();

        if (!string.IsNullOrWhiteSpace(copyStatus))
        {
            ImGui.SameLine();
            ImGui.TextDisabled(copyStatus);
        }

        ImGui.Spacing();
        ImGui.TextColored(PluginTheme.Gold, "DIAGNOSTICS");
        ImGui.Separator();

        if (snapshot is null)
        {
            ImGui.TextWrapped("Select Run Checks to inspect the current safe, read-only plugin state.");
            return;
        }

        DrawValue("Plugin", $"{snapshot.PublicName} ({snapshot.InternalName})");
        DrawValue("Version", snapshot.Version);
        DrawValue("Loaded build", snapshot.LoadedBuildId?.ToString() ?? "Not observed");
        DrawValue("Dalamud API", snapshot.DalamudApiLevel.ToString(CultureInfo.InvariantCulture));
        DrawValue("Channel", snapshot.Channel);
        DrawValue("Checked", $"{snapshot.CheckedAt.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC");

        ImGui.Spacing();
        ImGui.TextColored(PluginTheme.Gold, "CHECKS");
        ImGui.Separator();
        ImGui.BeginChild("##diagnostic-checks", Vector2.Zero, true);
        if (snapshot.Operation is { } operation)
        {
            DrawValue("Current phase", operation.Phase);
            ImGui.TextWrapped(operation.Status);
            DrawValue("Confirmed actions", operation.ConfirmedActions.ToString(CultureInfo.InvariantCulture));
            if (operation.LastFailure is { } failure)
            {
                ImGui.Separator();
                ImGui.TextWrapped($"Last failure: {failure.AtUtc:O} ({failure.Phase})");
                ImGui.TextWrapped(failure.Reason);
                foreach (var check in failure.Checks)
                    ImGui.TextWrapped($"{check.Name}: {check.Passed?.ToString() ?? "Not observed"}; expected {check.Expected}; observed {check.Observed}");
            }
            ImGui.Separator();
        }
        foreach (var check in snapshot.Checks)
        {
            ImGui.TextColored(GetResultColor(check.Outcome), Format(check.Outcome));
            ImGui.SameLine();
            ImGui.TextUnformatted(check.Name);
            ImGui.TextWrapped(check.Detail);
            if (!string.IsNullOrWhiteSpace(check.Recovery))
                ImGui.TextDisabled($"Recovery: {check.Recovery}");
            ImGui.Spacing();
        }

        ImGui.Separator();
        ImGui.TextWrapped($"Report problems at {PluginIdentity.IssueUrl}");
        ImGui.TextWrapped("Copy Diagnostics excludes private identifiers and paths. Review before sharing.");
        ImGui.EndChild();
    }

    private static void DrawValue(string label, string value)
    {
        ImGui.TextDisabled(label + ":");
        ImGui.SameLine();
        ImGui.TextUnformatted(value);
    }

    private static string Format(DiagnosticResultKind result)
        => result == DiagnosticResultKind.NotAvailable ? "Not Available" : result.ToString();

    private static Vector4 GetResultColor(DiagnosticResultKind result)
        => result switch
        {
            DiagnosticResultKind.Pass => new Vector4(0.55f, 0.82f, 0.55f, 1f),
            DiagnosticResultKind.Warning => PluginTheme.Gold,
            DiagnosticResultKind.Failure => new Vector4(0.92f, 0.45f, 0.42f, 1f),
            DiagnosticResultKind.Waiting => new Vector4(0.60f, 0.75f, 0.92f, 1f),
            _ => PluginTheme.TextMuted,
        };

}
