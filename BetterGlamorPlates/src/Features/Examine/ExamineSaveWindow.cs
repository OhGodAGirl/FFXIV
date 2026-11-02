using System.Numerics;
using BetterGlamorPlates.Adapters;
using BetterGlamorPlates.Features.Main;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;

namespace BetterGlamorPlates.Features.Examine;

/// <summary>Attached Examine action; receives copied facts and emits an explicit save request.</summary>
internal sealed class ExamineSaveWindow(IDalamudPluginInterface pluginInterface, Action<ExamineView> save) : Window(
    "Save examined glamour##BetterGlamorPlates", ImGuiWindowFlags.AlwaysAutoResize
    | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings
    | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav), IDisposable
{
    private readonly IFontHandle buttonFont = pluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(
        new GameFontStyle(GameFontFamily.Axis, 22f));
    private bool disposed;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        buttonFont.Dispose();
    }

    internal ExamineView? View { get; set; }
    internal ExamineButtonTheme Theme { get; set; } = ExamineButtonTheme.Resolve(0);
    internal bool Saving { get; set; }
    internal string Status { get; set; } = string.Empty;

    private Vector2? TabPosition()
    {
        var viewport = ImGui.GetMainViewport();
        return ExaminePresentation.Position(View,
            new WindowBounds(viewport.WorkPos.X, viewport.WorkPos.Y, viewport.WorkSize.X, viewport.WorkSize.Y));
    }

    public override bool DrawConditions() => TabPosition() is not null;

    public override void PreDraw()
    {
        if (TabPosition() is { } position) ImGui.SetNextWindowPos(position, ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, Vector2.Zero);
    }

    public override void PostDraw() => ImGui.PopStyleVar(3);

    public override void Draw()
    {
        if (View is not { } view) return;
        var size = ExamineTabPlacement.ButtonSize(view.Bounds);
        using var font = buttonFont.Push();
        ImGui.SetWindowFontScale(1);
        var textScale = 22 * ExamineTabPlacement.Scale(view.Bounds) / ImGui.GetFontSize();
        ImGui.SetWindowFontScale(textScale);
        var saved = ExaminePresentation.IsSaved(Status);
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Background);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Hovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Active);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.Border);
        ImGui.PushStyleColor(ImGuiCol.Text, saved ? Theme.SavedIcon : Theme.Icon);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Theme.Rounding * ExamineTabPlacement.Scale(view.Bounds));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGui.BeginDisabled(!ExaminePresentation.CanSave(view, Saving));
        using (pluginInterface.UiBuilder.IconFontHandle.Push())
        {
            ImGui.SetWindowFontScale(1);
            ImGui.SetWindowFontScale(32 * ExamineTabPlacement.Scale(view.Bounds) / ImGui.GetFontSize());
            var icon = saved ? FontAwesomeIcon.Check : FontAwesomeIcon.Save;
            if (ImGui.Button(icon.ToIconString() + "##save-examined-glamour", size))
                save(view);
        }
        ImGui.SetWindowFontScale(textScale);
        ImGui.EndDisabled();
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(5);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 320 * ImGuiHelpers.GlobalScale);
            ImGui.TextUnformatted("Save Glamour");
            if (!string.IsNullOrEmpty(view.Name)) ImGui.TextUnformatted($"{view.Name}'s Glamour");
            var message = ExaminePresentation.Message(view, Saving, Status);
            if (!string.IsNullOrEmpty(message)) ImGui.TextUnformatted(message);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }
}

