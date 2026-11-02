using System.Numerics;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Shared.Ui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;

namespace BetterGlamorPlates.Features.Main;

internal sealed partial class MainWindow
{
    private float designScale = 1f;
    private float bodyFontSize;
    private float S(float value) => value * designScale;

    public override void Draw()
    {
        ImGui.SetWindowFontScale(designScale / ImGuiHelpers.GlobalScale);
        bodyFontSize = ImGui.GetFontSize();
        var view = controller.View;
        DrawHeader();
        var width = ImGui.GetWindowSize().X - S(20);
        var height = ImGui.GetWindowSize().Y - S(64);
        var origin = ImGui.GetWindowPos() + new Vector2(S(10), S(54));
        var layout = MainLayoutPolicy.Calculate(width / designScale, height / designScale);

        ImGui.SetCursorScreenPos(origin);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        var libraryVisible = ImGui.BeginChild("##library", new Vector2(S(layout.RailWidth), height), true,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleColor();
        if (libraryVisible)
        {
            ImGui.SetWindowFontScale(1f);
            DrawLibrary(view);
        }
        ImGui.EndChild();

        ImGui.SetCursorScreenPos(origin + new Vector2(S(layout.RailWidth + MainLayoutPolicy.PaneGap), 0));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        var workspaceVisible = ImGui.BeginChild("##workspace", new Vector2(S(layout.MainWidth), height), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        if (workspaceVisible) DrawWorkspace(view, layout);
        ImGui.EndChild();
        DrawDyePicker(view);
        DrawIconPicker(view);
    }

    private void DrawHeader()
    {
        var position = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilledMultiColor(position + new Vector2(S(3)), position + size - new Vector2(S(3)),
            ImGui.GetColorU32(PluginTheme.WindowBackground), ImGui.GetColorU32(PluginTheme.WindowBackground),
            ImGui.GetColorU32(PluginTheme.PearlMint), ImGui.GetColorU32(PluginTheme.PearlMint));
        draw.AddRect(position + new Vector2(2), position + size - new Vector2(2),
            ImGui.GetColorU32(PluginTheme.FrameIvory), S(7), ImDrawFlags.None, S(3));
        var top = position + new Vector2(S(4));
        var bottom = position + new Vector2(size.X - S(4), S(44));
        draw.AddRectFilledMultiColor(top, bottom, ImGui.GetColorU32(PluginTheme.FrameIvory),
            ImGui.GetColorU32(PluginTheme.FrameIvory), ImGui.GetColorU32(PluginTheme.FrameShadow),
            ImGui.GetColorU32(PluginTheme.FrameShadow));
        ImGui.SetCursorScreenPos(position + new Vector2(S(16), S(10)));
        using (titleFont.Push())
            ImGui.TextColored(PluginTheme.PearlText, "Better Glamour Plates");
        ImGui.SetCursorScreenPos(position + new Vector2(size.X - S(40), S(7)));
        if (ImGui.Button("×##close-companion", new Vector2(S(30))))
            IsOpen = false;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Close Better Glamour Plates");
    }

    private void DrawWorkspace(MainViewState view, MainLayoutMetrics layout)
    {
        var origin = ImGui.GetCursorScreenPos();
        var feedback = LoadQueued || view.OperationFeedback is not null || view.CollectionNeedsAttention;
        var footerHeight = MainLayoutPolicy.FooterSize(layout.MainWidth - 24, feedback);
        var collectionHeight = layout.CollectionPanelHeight - (footerHeight - MainLayoutPolicy.FooterHeight);
        if (ImGui.BeginChild("##equipment", new Vector2(0, S(layout.PlatePanelHeight)), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            DrawPlateHeader(view);
            DrawEquipment(view);
            DrawOutfitActions(view);

        }
        ImGui.EndChild();
        ImGui.SetCursorScreenPos(origin + new Vector2(0, S(layout.PlatePanelHeight + MainLayoutPolicy.PaneGap)));
        DrawCollectionPanel(view, S(collectionHeight));
        ImGui.SetCursorScreenPos(origin + new Vector2(0,
            S(layout.PlatePanelHeight + MainLayoutPolicy.PaneGap + collectionHeight + MainLayoutPolicy.PaneGap)));
        DrawFooter(view, footerHeight);
    }

    private void DrawPlateHeader(MainViewState view)
    {
        var origin = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddRectFilled(ImGui.GetWindowPos(),
            new Vector2(ImGui.GetWindowPos().X + ImGui.GetWindowWidth(), origin.Y + S(27)),
            ImGui.GetColorU32(PluginTheme.WindowBackground));
        DrawSectionHeading(FitText((view.HasUnsavedChanges ? "* " : "") + (view.ActivePlate?.Name ?? "Outfit"), ImGui.GetContentRegionAvail().X));
        ImGui.SetCursorScreenPos(origin + new Vector2(0, S(32)));
    }
    private void DrawOutfitActions(MainViewState view)
    {
        ImGui.SetCursorPos(new Vector2(ImGui.GetWindowWidth() - S(124), ImGui.GetWindowHeight() - S(40)));
        ImGui.BeginDisabled(view.ActivePlate is null || !view.CanLoad || LoadQueued);
        if (IconButton("clear-outfit-items", FontAwesomeIcon.Eraser, "Clear Items", S(32)))
            IssueDrawerCommand(OutfitInteraction.ClearItems(view, LoadQueued));
        ImGui.EndDisabled();
        ImGui.SameLine(0, S(8));
        ImGui.BeginDisabled(!view.HasUnsavedChanges);
        if (IconButton("save-outfit", FontAwesomeIcon.Save, "Save Changes", S(32)))
            IssueDrawerCommand(OutfitInteraction.Save(view));
        ImGui.SameLine(0, S(8));
        if (IconButton("reset-outfit", FontAwesomeIcon.Undo, "Reset Changes", S(32)))
            IssueDrawerCommand(OutfitInteraction.Reset(view));
        ImGui.EndDisabled();
    }

    private void DrawFooter(MainViewState view, float footerHeight)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        var footerVisible = ImGui.BeginChild("##footer", new Vector2(0, S(footerHeight)), true,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleColor();
        if (!footerVisible)
        {
            ImGui.EndChild();
            return;
        }
        var origin = ImGui.GetWindowPos() + new Vector2(S(12), 0);
        var width = ImGui.GetWindowWidth() - S(24);
        var compact = width < S(650);
        var controlsHeight = S(compact ? 90 : MainLayoutPolicy.FooterHeight);
        var feedback = OutfitInteraction.Feedback(view, LoadQueued);
        var centreY = compact ? S(23) : controlsHeight / 2;
        ImGui.SetCursorScreenPos(origin + new Vector2(0, centreY - ImGui.GetFontSize() / 2));
        ImGui.TextColored(PluginTheme.PearlText, "Preview:");
        var labelWidth = ImGui.CalcTextSize("Preview:").X;
        ImGui.SetCursorScreenPos(origin + new Vector2(labelWidth + S(8), centreY - S(15)));
        DrawPreviewMode(view, "Dyed", PlateDyeMode.Saved);
        ImGui.SameLine(0, 0);
        DrawPreviewMode(view, "Undyed", PlateDyeMode.None);
        var previewWidth = labelWidth + S(172);
        var loadWidth = compact ? S(236) : Math.Min(S(236), width - 2 * (previewWidth + S(8)));
        loadWidth = Math.Max(S(170), loadWidth);
        if (!view.CollectionNeedsAttention)
        {
            var label = $"{view.CollectionCount:N0} Owned";
            var labelSize = ImGui.CalcTextSize(label);
            ImGui.SetCursorScreenPos(origin + new Vector2(width - labelSize.X,
                (compact ? S(65) : centreY) - labelSize.Y / 2));
            ImGui.TextColored(PluginTheme.PearlText, label);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(label);
        }
        ImGui.SetCursorScreenPos(origin + new Vector2(compact ? 0 : (width - loadWidth) / 2,
            (compact ? S(65) : centreY) - S(18)));
        if (LoadQueued)
        {
            if (ImGui.Button("Cancel Load", new Vector2(loadWidth, S(34)))) CancelLoad?.Invoke();
        }
        else if (PrimaryActionButton.Draw("Load Into Current Plate", new Vector2(loadWidth, S(36)), view.CanLoad))
            IssueDrawerCommand(OutfitInteraction.Load(view, LoadQueued));
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ShowWrappedTooltip(view.CanLoad
                ? "Load available pieces using the preview colours. Missing pieces are skipped; empty outfit slots clear native slots. Native Save remains manual."
                : view.LoadDisabledReason ?? "Select a saved plate first.");
        if (feedback is not null)
        {
            ImGui.SetCursorScreenPos(origin + new Vector2(0, controlsHeight));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
            var feedbackVisible = ImGui.BeginChild("##load-feedback", new Vector2(width, S(82)), false);
            ImGui.PopStyleColor();
            if (feedbackVisible)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, PluginTheme.PearlWarning);
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
                ImGui.TextUnformatted(feedback);
                ImGui.PopTextWrapPos();
                ImGui.PopStyleColor();
            }
            ImGui.EndChild();
        }
        ImGui.EndChild();
    }

    private void ShowWrappedTooltip(string text)
    {
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Min(S(360), ImGui.GetMainViewport().WorkSize.X - S(32)));
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    private void DrawPreviewMode(MainViewState view, string label, PlateDyeMode mode)
    {
        var selected = view.PreviewDyeMode == mode;
        if (selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, PluginTheme.PrimaryAction);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, PluginTheme.PrimaryActionHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, PluginTheme.PrimaryActionActive);
            ImGui.PushStyleColor(ImGuiCol.Text, PluginTheme.PearlText);
        }
        if (ImGui.Button(label, new Vector2(S(82), S(30))))
            issueCommand(new MainCommand.PreviewActive(mode));
        if (selected) ImGui.PopStyleColor(4);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Use these colours for preview and the next explicit load. Saved outfit colours are retained.");
    }

    private static string FitText(string text, float width)
        => UiText.Fit(text, width, value => ImGui.CalcTextSize(value).X);

    private void OutlineLastItem()
        => ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(),
            ImGui.GetColorU32(PluginTheme.Gold), S(4), ImDrawFlags.None, S(1.5f));
}


