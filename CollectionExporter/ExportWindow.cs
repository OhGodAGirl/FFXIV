using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace CollectionExporter;

public sealed class ExportWindow : Window, IDisposable
{
    private const float ResponsiveBreakpoint = 620f;
    private const string DresserName = "Glamour Dresser";
    private const string ArmoireName = "Armoire";
    private const float InteractionDistance = 2.5f;

    private static readonly Vector4 WindowBackground = new(0.18f, 0.182f, 0.185f, 0.99f);
    private static readonly Vector4 PanelBackground = new(0.19f, 0.20f, 0.208f, 1f);
    private static readonly Vector4 FrameBackground = new(0.32f, 0.32f, 0.32f, 1f);
    private static readonly Vector4 Gold = new(0.82f, 0.70f, 0.43f, 1f);
    private static readonly Vector4 GoldMuted = new(0.48f, 0.42f, 0.30f, 1f);
    private static readonly Vector4 Text = new(0.90f, 0.89f, 0.85f, 1f);
    private static readonly Vector4 MutedText = new(0.62f, 0.61f, 0.57f, 1f);
    private static readonly Vector4 Success = new(0.57f, 0.78f, 0.56f, 1f);
    private static readonly Vector4 Error = new(0.92f, 0.48f, 0.44f, 1f);
    private static readonly Vector4 Warning = new(0.92f, 0.72f, 0.34f, 1f);

    private static readonly Dictionary<string, string> CollectionNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["armoires"] = "Armoire",
            ["bardings"] = "Bardings",
            ["cards"] = "Triple Triad Cards",
            ["fashions"] = "Fashion Accessories",
            ["field_records"] = "Field Records",
            ["frames"] = "Portrait Frames",
            ["hairstyles"] = "Hairstyles",
            ["leves"] = "Completed Leves",
            ["npcs"] = "Triple Triad NPCs",
            ["occult_records"] = "Occult Records",
            ["orchestrions"] = "Orchestrion Rolls",
            ["outfits"] = "Outfits",
            ["relics"] = "Relics",
            ["spells"] = "Blue Magic",
            ["survey_records"] = "Variant Dungeon Records",
        };

    private readonly Plugin plugin;
    private readonly IFontHandle sectionFont;
    private ExportResult? result;
    private readonly byte[] profileBuffer = new byte[256];
    private string status = "Ready to read this character's collections.";
    private StatusKind statusKind = StatusKind.Information;
    private bool generating;
    private bool stylesPushed;
    private GuidedExportPhase guidedPhase;
    private Task<bool>? guidedGenerationTask;
    private DateTime nextGuidedActionAt;
    private DateTime nextInteractionAt;
    private int interactionAttempts;
    private bool menuSelectionIssued;
    private bool? sizedForResult;
    private bool clearForcedSizeCondition;
    private string profileFeedback = string.Empty;
    private bool profileFeedbackIsError;

    private bool IsGuidedExportActive => guidedPhase is
        not GuidedExportPhase.Idle
        and not GuidedExportPhase.Complete
        and not GuidedExportPhase.Paused
        and not GuidedExportPhase.Cancelled;

    public ExportWindow(Plugin plugin)
        : base("Collection Exporter###CollectionExporter")
    {
        this.plugin = plugin;
        SetProfileBuffer(plugin.ConfiguredProfile);
        using (Plugin.PluginInterface.UiBuilder.FontAtlas.SuppressAutoRebuild())
        {
            sectionFont = Plugin.PluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(
                new GameFontStyle(GameFontFamily.MiedingerMid, 17f));
        }

        Size = new Vector2(760, 300);
        SizeCondition = ImGuiCond.Always;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(680, 280),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose()
    {
        sectionFont.Dispose();
    }

    public override void PreDraw()
    {
        var hasResult = result is not null;
        if (sizedForResult != hasResult)
        {
            Size = new Vector2(760, hasResult ? 440 : 300);
            SizeCondition = ImGuiCond.Always;
            sizedForResult = hasResult;
            clearForcedSizeCondition = true;
        }
        else if (clearForcedSizeCondition)
        {
            SizeCondition = ImGuiCond.None;
            clearForcedSizeCondition = false;
        }

        ImGui.SetNextWindowBgAlpha(WindowBackground.W);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 3f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 3f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 10f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(7f, 4f));
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(6f, 4f));
        ImGui.PushStyleColor(ImGuiCol.Text, Text);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, MutedText);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, WindowBackground);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, PanelBackground);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, PanelBackground);
        ImGui.PushStyleColor(ImGuiCol.Border, GoldMuted);
        ImGui.PushStyleColor(ImGuiCol.BorderShadow, new Vector4(0.035f, 0.035f, 0.035f, 0.9f));
        ImGui.PushStyleColor(ImGuiCol.Separator, GoldMuted);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.23f, 0.235f, 0.23f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.27f, 0.278f, 0.27f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, new Vector4(0.20f, 0.205f, 0.20f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, FrameBackground);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.37f, 0.37f, 0.365f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.42f, 0.415f, 0.40f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.28f, 0.282f, 0.28f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.38f, 0.365f, 0.32f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.47f, 0.42f, 0.32f, 1f));
        ImGui.PushStyleColor(ImGuiCol.CheckMark, Gold);
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, new Vector4(0.29f, 0.295f, 0.295f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, GoldMuted);
        ImGui.PushStyleColor(ImGuiCol.TableBorderLight, new Vector4(0.36f, 0.36f, 0.35f, 0.75f));
        ImGui.PushStyleColor(ImGuiCol.TableRowBg, new Vector4(0.19f, 0.195f, 0.20f, 0.92f));
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, new Vector4(0.225f, 0.23f, 0.235f, 0.92f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0.13f, 0.135f, 0.14f, 0.9f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0.38f, 0.375f, 0.35f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0.50f, 0.44f, 0.32f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Gold);
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.35f, 0.31f, 0.235f, 0.9f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.46f, 0.39f, 0.27f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.54f, 0.44f, 0.28f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, new Vector4(0.55f, 0.44f, 0.24f, 0.55f));
        stylesPushed = true;
    }

    public override void PostDraw()
    {
        if (!stylesPushed)
            return;

        ImGui.PopStyleColor(31);
        ImGui.PopStyleVar(8);
        stylesPushed = false;
    }

    public override void Draw()
    {
        DrawHeader();
        DrawProfileSection();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawActionsRow();

        if (result is not null)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            DrawResultSection(result);
        }

    }

    private void DrawActionsRow()
    {
        if (ImGui.GetContentRegionAvail().X < ResponsiveBreakpoint)
        {
            DrawGuidedExportSection();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            DrawGenerateSection();
            return;
        }

        if (!ImGui.BeginTable(
                "##actionsRow",
                2,
                ImGuiTableFlags.SizingStretchProp
                | ImGuiTableFlags.PadOuterX
                | ImGuiTableFlags.BordersInnerV
                | ImGuiTableFlags.NoSavedSettings))
        {
            return;
        }

        ImGui.TableSetupColumn("Guided", ImGuiTableColumnFlags.WidthStretch, 1.15f);
        ImGui.TableSetupColumn("Generate", ImGuiTableColumnFlags.WidthStretch, 0.85f);
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        DrawGuidedExportSection();
        ImGui.TableSetColumnIndex(1);
        DrawGenerateSection();
        ImGui.EndTable();
    }

    private void DrawHeader()
    {
        if (ImGui.GetContentRegionAvail().X >= ResponsiveBreakpoint
            && ImGui.BeginTable(
                "##headerRow",
                2,
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings))
        {
            ImGui.TableSetupColumn("Description", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 170f);
            ImGui.TableNextColumn();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 7f);
            ImGui.TextColored(
                MutedText,
                "A small helper to export your collections in JSON compatible with FFXIV Collect");
            ImGui.TableNextColumn();
            DrawStatusPane();
            ImGui.EndTable();
        }
        else
        {
            ImGui.TextColored(
                MutedText,
                "A small helper to export your collections in JSON compatible with FFXIV Collect");
            ImGui.Spacing();
            DrawStatusPane();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    private void DrawStatusPane()
    {
        var (label, color) = GetDisplayStatus();
        var size = new Vector2(160f, 32f);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(size);
        var max = min + size;
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(PanelBackground), 10f);
        drawList.AddRect(min, max, ImGui.GetColorU32(color), 10f, ImDrawFlags.RoundCornersAll, 1.25f);
        var labelSize = ImGui.CalcTextSize(label);
        drawList.AddText(
            ((min + max) * 0.5f) - (labelSize * 0.5f),
            ImGui.GetColorU32(color),
            label);
    }

    private (string Label, Vector4 Color) GetDisplayStatus()
    {
        if (generating || IsGuidedExportActive)
            return ("IN PROGRESS", Gold);

        if (guidedPhase is GuidedExportPhase.Paused or GuidedExportPhase.Cancelled)
            return ("WAITING", statusKind == StatusKind.Error ? Error : MutedText);

        return result is not null
            ? ("COMPLETE", Success)
            : ("WAITING", MutedText);
    }

    private void DrawProfileSection()
    {
        DrawInlineSectionHeading("PROFILE", "Optional - adds Outfits and Relics");

        var controlsBusy = generating || IsGuidedExportActive;
        if (controlsBusy)
            ImGui.BeginDisabled();

        var compact = ImGui.GetContentRegionAvail().X < ResponsiveBreakpoint;
        var saveWidth = ImGui.CalcTextSize("Save Profile").X
            + (ImGui.GetStyle().FramePadding.X * 2f)
            + 12f;
        var clearWidth = ImGui.CalcTextSize("Clear").X
            + (ImGui.GetStyle().FramePadding.X * 2f)
            + 12f;
        if (!compact
            && ImGui.BeginTable(
                "##profileControls",
                3,
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings))
        {
            ImGui.TableSetupColumn("Profile", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Save", ImGuiTableColumnFlags.WidthFixed, saveWidth);
            ImGui.TableSetupColumn("Clear", ImGuiTableColumnFlags.WidthFixed, clearWidth);
            ImGui.TableNextColumn();
            DrawProfileInput();
            ImGui.TableNextColumn();
            DrawSaveProfileButton();
            ImGui.TableNextColumn();
            DrawClearProfileButton();
            ImGui.EndTable();
        }
        else
        {
            DrawProfileInput();
            ImGui.Spacing();
            DrawSaveProfileButton(saveWidth);
            ImGui.SameLine();
            DrawClearProfileButton(clearWidth);
        }

        if (controlsBusy)
            ImGui.EndDisabled();

        if (!string.IsNullOrEmpty(profileFeedback))
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, profileFeedbackIsError ? Error : Success);
            ImGui.TextWrapped(profileFeedback);
            ImGui.PopStyleColor();
        }
    }

    private void DrawProfileInput()
    {
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint(
            "##profile",
            "Public FFXIV Collect character URL or ID",
            profileBuffer.AsSpan());
    }

    private void DrawSaveProfileButton(float width = -1f)
    {
        if (!ImGui.Button("Save Profile", new Vector2(width, 0f)))
            return;

        var input = ReadProfileBuffer();
        var saved = plugin.TrySetProfile(input, out var message);
        profileFeedback = message;
        profileFeedbackIsError = !saved;
        statusKind = saved ? StatusKind.Success : StatusKind.Error;
        if (saved)
            result = null;
    }

    private void DrawClearProfileButton(float width = -1f)
    {
        var hasSavedProfile = !string.IsNullOrEmpty(plugin.ConfiguredProfile);
        if (!hasSavedProfile)
            ImGui.BeginDisabled();
        if (ImGui.Button("Clear", new Vector2(width, 0f)))
        {
            plugin.ClearProfile();
            SetProfileBuffer(string.Empty);
            result = null;
            status = "Saved profile removed. Outfits and Relics will be omitted.";
            statusKind = StatusKind.Success;
            profileFeedback = "Saved profile removed. Outfits and Relics will be omitted.";
            profileFeedbackIsError = false;
        }
        if (!hasSavedProfile)
            ImGui.EndDisabled();
    }

    private void DrawGuidedExportSection()
    {
        DrawInlineSectionHeading("GUIDED EXPORT", "Dresser, then Armoire");

        var availableWidth = ImGui.GetContentRegionAvail().X;
        if (IsGuidedExportActive)
        {
            if (DrawNativeButton("Cancel Guided Export", new Vector2(availableWidth, 36), generating, Warning))
                CancelGuidedExport();
        }
        else if (DrawNativeButton("Start Guided Export", new Vector2(availableWidth, 36), generating))
        {
            StartGuidedExport();
        }

        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, MutedText);
        ImGui.TextWrapped("Move when prompted; the plugin does not control your character.");
        ImGui.PopStyleColor();
        if (guidedPhase != GuidedExportPhase.Idle)
        {
            ImGui.Spacing();
            ImGui.TextWrapped(status);
        }
    }

    private void DrawGenerateSection()
    {
        DrawInlineSectionHeading("CURRENT VIEW", "Read the available collection state now");

        var availableWidth = ImGui.GetContentRegionAvail().X;
        if (IsGuidedExportActive || generating)
            ImGui.BeginDisabled();
        if (ImGui.Button(
                generating ? "Reading Collections..." : "Generate Current View",
                new Vector2(availableWidth, 36)))
            Generate();
        if (IsGuidedExportActive || generating)
            ImGui.EndDisabled();

        if (result is not null)
        {
            ImGui.Spacing();
            ImGui.TextColored(MutedText, "Your completed export is ready below.");
        }

        if (statusKind == StatusKind.Error && !IsGuidedExportActive)
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, Error);
            ImGui.TextWrapped(status);
            ImGui.PopStyleColor();
        }
    }

    private void DrawResultSection(ExportResult exportResult)
    {
        if (ImGui.BeginTable(
                "##summaryHeader",
                2,
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings))
        {
            ImGui.TableSetupColumn("Summary", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Copy", ImGuiTableColumnFlags.WidthFixed, 145f);
            ImGui.TableNextColumn();
            DrawInlineSectionHeading(
                "EXPORT SUMMARY",
                $"{exportResult.Collections.Count} collections");
            ImGui.TableNextColumn();
            if (DrawNativeButton("Copy JSON", new Vector2(145f, 34f), false))
                CopyJson(exportResult);
            ImGui.EndTable();
        }

        var availableWidth = ImGui.GetContentRegionAvail().X;
        var columnCount = availableWidth >= 680f ? 3 : availableWidth >= 480f ? 2 : 1;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(10, 6));
        if (ImGui.BeginTable(
                "##collectionCounts",
                columnCount,
                ImGuiTableFlags.SizingStretchSame
                | ImGuiTableFlags.PadOuterX
                | ImGuiTableFlags.RowBg
                | ImGuiTableFlags.NoSavedSettings))
        {
            foreach (var pair in exportResult.Collections)
            {
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(GetCollectionName(pair.Key));
                ImGui.SameLine();
                ImGui.TextColored(Gold, pair.Value.Length.ToString(CultureInfo.InvariantCulture));
            }

            ImGui.EndTable();
        }
        ImGui.PopStyleVar();

        if (exportResult.OmittedCollections.Count > 0)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextColored(Error, "ERROR");
            foreach (var pair in exportResult.OmittedCollections)
            {
                ImGui.Bullet();
                ImGui.SameLine();
                ImGui.TextWrapped($"{GetCollectionName(pair.Key)} - {pair.Value}");
            }
        }

    }

    private void CopyJson(ExportResult exportResult)
    {
        ImGui.SetClipboardText(exportResult.Json);
        status = "Import data copied to the clipboard.";
        statusKind = StatusKind.Success;
    }

    private void DrawInlineSectionHeading(string title, string description)
    {
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 4f);
        ImGui.PushStyleColor(ImGuiCol.Text, Gold);
        using (sectionFont.Push())
            ImGui.TextUnformatted(title);
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.TextColored(MutedText, description);
        ImGui.Spacing();
    }

    private static bool DrawNativeButton(
        string label,
        Vector2 size,
        bool disabled,
        Vector4? labelColor = null)
    {
        if (disabled)
            ImGui.BeginDisabled();
        var clicked = ImGui.InvisibleButton($"##{label}", size);
        if (disabled)
            ImGui.EndDisabled();

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var hovered = !disabled && ImGui.IsItemHovered();
        var held = !disabled && ImGui.IsItemActive();
        var drawList = ImGui.GetWindowDrawList();
        var pressOffset = held ? new Vector2(0f, 1.5f) : Vector2.Zero;
        var rounding = MathF.Min(12f, size.Y * 0.36f);
        var rim = disabled
            ? new Vector4(0.34f, 0.33f, 0.30f, 0.52f)
            : hovered || held
                ? new Vector4(0.72f, 0.62f, 0.42f, 1f)
                : new Vector4(0.49f, 0.45f, 0.36f, 1f);
        var faceTop = disabled
            ? new Vector4(0.25f, 0.25f, 0.24f, 0.68f)
            : held
                ? new Vector4(0.36f, 0.33f, 0.27f, 1f)
                : hovered
                    ? new Vector4(0.37f, 0.36f, 0.33f, 1f)
                    : new Vector4(0.31f, 0.31f, 0.30f, 1f);
        var faceBottom = disabled
            ? new Vector4(0.17f, 0.17f, 0.165f, 0.68f)
            : held
                ? new Vector4(0.24f, 0.22f, 0.18f, 1f)
                : hovered
                    ? new Vector4(0.23f, 0.225f, 0.21f, 1f)
                    : new Vector4(0.19f, 0.19f, 0.185f, 1f);

        drawList.AddRectFilled(min + new Vector2(1.5f, 3f), max + new Vector2(1.5f, 3f),
            ImGui.GetColorU32(new Vector4(0.015f, 0.015f, 0.012f, 0.72f)), rounding);
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0.10f, 0.10f, 0.095f, 1f)),
            rounding);
        var rimMin = min + new Vector2(1f, 1f) + pressOffset;
        var rimMax = max - new Vector2(1f, 1f) + pressOffset;
        drawList.AddRectFilled(rimMin, rimMax, ImGui.GetColorU32(rim), rounding - 1f);
        var faceMin = min + new Vector2(2.5f, 2.5f) + pressOffset;
        var faceMax = max - new Vector2(2.5f, 2.5f) + pressOffset;
        drawList.AddRectFilled(faceMin, faceMax, ImGui.GetColorU32(faceBottom), rounding - 2.5f);
        var gradientMin = faceMin + new Vector2(3f, 2f);
        var gradientMax = faceMax - new Vector2(3f, 2f);
        drawList.AddRectFilledMultiColor(
            gradientMin,
            gradientMax,
            ImGui.GetColorU32(faceTop),
            ImGui.GetColorU32(faceTop),
            ImGui.GetColorU32(faceBottom),
            ImGui.GetColorU32(faceBottom));
        drawList.AddRect(min, max, ImGui.GetColorU32(new Vector4(0.055f, 0.055f, 0.05f, 1f)), rounding,
            ImDrawFlags.RoundCornersAll, 1.25f);
        drawList.AddRect(rimMin, rimMax, ImGui.GetColorU32(rim), rounding - 1f,
            ImDrawFlags.RoundCornersAll, 1f);
        drawList.AddLine(
            faceMin + new Vector2(7f, 3f),
            new Vector2(faceMax.X - 7f, faceMin.Y + 3f),
            ImGui.GetColorU32(new Vector4(0.93f, 0.89f, 0.78f, disabled ? 0.10f : 0.34f)),
            1f);
        drawList.AddLine(
            new Vector2(faceMin.X + 7f, faceMax.Y - 2f),
            faceMax - new Vector2(7f, 2f),
            ImGui.GetColorU32(new Vector4(0.025f, 0.025f, 0.022f, 0.78f)),
            1.25f);
        var labelSize = ImGui.CalcTextSize(label);
        var labelPosition = ((min + max) * 0.5f) - (labelSize * 0.5f) + pressOffset;
        drawList.AddText(labelPosition + new Vector2(1f, 1.5f),
            ImGui.GetColorU32(new Vector4(0.02f, 0.02f, 0.018f, disabled ? 0.32f : 0.82f)), label);
        drawList.AddText(labelPosition,
            ImGui.GetColorU32(disabled
                ? MutedText
                : labelColor ?? new Vector4(0.93f, 0.92f, 0.88f, 1f)), label);
        return clicked && !disabled;
    }

    internal unsafe void UpdateGuidedExport()
    {
        if (!IsGuidedExportActive || DateTime.UtcNow < nextGuidedActionAt)
            return;

        if (!Plugin.PlayerState.IsLoaded)
        {
            PauseGuidedExport("The guided export stopped because no character is logged in.");
            return;
        }

        switch (guidedPhase)
        {
            case GuidedExportPhase.OpeningDresser:
                UpdateDresserStep();
                break;
            case GuidedExportPhase.CapturingDresser:
                UpdateCaptureStep(GuidedExportPhase.ClosingDresser, "Glamour Dresser captured. Closing it now.");
                break;
            case GuidedExportPhase.ClosingDresser:
                CloseDresserAndContinue();
                break;
            case GuidedExportPhase.OpeningArmoire:
                UpdateArmoireStep();
                break;
            case GuidedExportPhase.CapturingArmoire:
                UpdateCaptureStep(GuidedExportPhase.Complete, "Guided export complete. Review the summary, then copy the JSON.");
                break;
        }
    }

    private void StartGuidedExport()
    {
        if (generating || IsGuidedExportActive)
            return;

        if (!Plugin.PlayerState.IsLoaded)
        {
            status = "Log into a character before starting a guided export.";
            statusKind = StatusKind.Error;
            return;
        }

        plugin.ResetOnlineCollectionCache();
        result = null;
        guidedGenerationTask = null;
        guidedPhase = GuidedExportPhase.OpeningDresser;
        ResetInteractionState();
        status = "Step 1 of 2: Move near a Glamour Dresser. It will open automatically when you are in range.";
        statusKind = StatusKind.Information;
        nextGuidedActionAt = DateTime.UtcNow;
    }

    private void CancelGuidedExport()
    {
        guidedPhase = GuidedExportPhase.Cancelled;
        guidedGenerationTask = null;
        status = "Guided export cancelled. Any completed capture remains available below.";
        statusKind = StatusKind.Information;
    }

    private unsafe void UpdateDresserStep()
    {
        if (IsDresserReady())
        {
            BeginGuidedCapture(
                GuidedExportPhase.CapturingDresser,
                "Step 1 of 2: Reading the Glamour Dresser...",
                "Glamour Dresser capture created.");
            return;
        }

        if (IsAddonVisible("MiragePrismPrismBox"))
        {
            status = "Step 1 of 2: Waiting for the Glamour Dresser contents to load...";
            ShortGuidedDelay();
            return;
        }

        GuideToStation(DresserName, "Step 1 of 2");
    }

    private unsafe void CloseDresserAndContinue()
    {
        if (IsAddonVisible("MiragePrismPrismBox"))
        {
            var agent = AgentMiragePrismPrismBox.Instance();
            if (agent != null && agent->IsAgentActive())
                agent->Hide();

            status = "Glamour Dresser captured. Closing it before opening the Armoire...";
            GuidedDelay(500);
            return;
        }

        guidedPhase = GuidedExportPhase.OpeningArmoire;
        ResetInteractionState();
        status = "Step 2 of 2: Move near an Armoire. It will open automatically when you are in range.";
        statusKind = StatusKind.Information;
        ShortGuidedDelay();
    }

    private unsafe void UpdateArmoireStep()
    {
        if (IsArmoireReady())
        {
            BeginGuidedCapture(
                GuidedExportPhase.CapturingArmoire,
                "Step 2 of 2: Reading the Armoire...",
                "Armoire capture created.");
            return;
        }

        if (IsAddonVisible("Cabinet"))
        {
            status = "Step 2 of 2: Waiting for the Armoire contents to load...";
            ShortGuidedDelay();
            return;
        }

        if (IsAddonVisible("SelectString"))
        {
            if (!menuSelectionIssued || DateTime.UtcNow >= nextInteractionAt)
            {
                var address = Plugin.GameGui.GetAddonByName("SelectString");
                if (!address.IsNull)
                {
                    var addon = (AddonSelectString*)address.Address;
                    menuSelectionIssued = addon->AtkUnitBase.FireCallbackInt(0);
                    nextInteractionAt = DateTime.UtcNow.AddSeconds(1);
                }
            }

            status = "Step 2 of 2: Opening the Armoire's item view...";
            ShortGuidedDelay();
            return;
        }

        GuideToStation(ArmoireName, "Step 2 of 2");
    }

    private void BeginGuidedCapture(
        GuidedExportPhase capturePhase,
        string readingStatus,
        string completedStatus)
    {
        guidedPhase = capturePhase;
        guidedGenerationTask = GenerateAsync(readingStatus, completedStatus);
        ShortGuidedDelay();
    }

    private void UpdateCaptureStep(GuidedExportPhase nextPhase, string completedStatus)
    {
        if (guidedGenerationTask is null || !guidedGenerationTask.IsCompleted)
        {
            ShortGuidedDelay();
            return;
        }

        var succeeded = guidedGenerationTask.GetAwaiter().GetResult();
        guidedGenerationTask = null;
        if (!succeeded)
        {
            guidedPhase = GuidedExportPhase.Paused;
            return;
        }

        guidedPhase = nextPhase;
        status = completedStatus;
        statusKind = nextPhase == GuidedExportPhase.Complete
            ? StatusKind.Success
            : StatusKind.Information;
        ShortGuidedDelay();
    }

    private unsafe void GuideToStation(string stationName, string step)
    {
        var station = FindStation(stationName);
        if (station is null)
        {
            status = $"{step}: Move near a {stationName}. The plugin will target it when it becomes visible.";
            ShortGuidedDelay();
            return;
        }

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player is null)
        {
            PauseGuidedExport("The local player position is unavailable.");
            return;
        }

        Plugin.TargetManager.Target = station;
        var distance = Vector3.Distance(player.Position, station.Position);
        if (distance > InteractionDistance)
        {
            status = FormattableString.Invariant(
                $"{step}: Move closer to the targeted {stationName} ({distance:F1} yalms away). Movement is left to you.");
            ShortGuidedDelay();
            return;
        }

        if (DateTime.UtcNow >= nextInteractionAt)
        {
            var targetSystem = TargetSystem.Instance();
            if (targetSystem == null)
            {
                PauseGuidedExport($"The {stationName} could not be interacted with.");
                return;
            }

            targetSystem->InteractWithObject((GameObject*)station.Address, true);
            interactionAttempts++;
            nextInteractionAt = DateTime.UtcNow.AddMilliseconds(900);
        }

        status = interactionAttempts <= 1
            ? $"{step}: Opening the {stationName}..."
            : $"{step}: Waiting for the {stationName} to open; attempt {interactionAttempts}.";
        ShortGuidedDelay();
    }

    private static IGameObject? FindStation(string stationName)
    {
        var target = Plugin.TargetManager.Target;
        if (target is not null
            && target.IsValid()
            && target.Name.TextValue.Contains(stationName, StringComparison.OrdinalIgnoreCase))
        {
            return target;
        }

        var player = Plugin.ObjectTable.LocalPlayer;
        return Plugin.ObjectTable
            .Where(gameObject => gameObject is not null
                && gameObject.IsValid()
                && gameObject.Name.TextValue.Contains(stationName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(gameObject => player is null
                ? float.MaxValue
                : Vector3.Distance(player.Position, gameObject.Position))
            .FirstOrDefault();
    }

    private static unsafe bool IsDresserReady()
    {
        var manager = MirageManager.Instance();
        return IsAddonVisible("MiragePrismPrismBox")
            && manager != null
            && manager->PrismBoxLoaded;
    }

    private static unsafe bool IsArmoireReady()
    {
        var uiState = UIState.Instance();
        return IsAddonVisible("Cabinet")
            && uiState != null
            && uiState->Cabinet.IsCabinetLoaded();
    }

    private static bool IsAddonVisible(string name)
    {
        var addon = Plugin.GameGui.GetAddonByName(name);
        return !addon.IsNull && addon.IsVisible;
    }

    private void PauseGuidedExport(string reason)
    {
        guidedPhase = GuidedExportPhase.Paused;
        guidedGenerationTask = null;
        status = reason;
        statusKind = StatusKind.Error;
    }

    private void ResetInteractionState()
    {
        interactionAttempts = 0;
        menuSelectionIssued = false;
        nextInteractionAt = DateTime.MinValue;
    }

    private void ShortGuidedDelay() => GuidedDelay(150);

    private void GuidedDelay(int milliseconds) =>
        nextGuidedActionAt = DateTime.UtcNow.AddMilliseconds(milliseconds);

    private async void Generate()
    {
        await GenerateAsync(
            "Reading collection state from the currently open view...",
            "Import data created from the currently available collection state.");
    }

    private async Task<bool> GenerateAsync(string readingStatus, string completedStatus)
    {
        generating = true;
        status = readingStatus;
        statusKind = StatusKind.Information;
        try
        {
            await plugin.RefreshOnlineCollectionsAsync();
            result = plugin.BuildExport();
            status = $"{completedStatus} {result.Collections.Count} collections are included.";
            statusKind = StatusKind.Success;
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to build FFXIV Collect export.");
            result = null;
            status = ex.Message;
            statusKind = StatusKind.Error;
            return false;
        }
        finally
        {
            generating = false;
        }
    }

    private static string GetCollectionName(string key) =>
        CollectionNames.TryGetValue(key, out var name) ? name : key.Replace('_', ' ');

    private string ReadProfileBuffer()
    {
        var nullIndex = Array.IndexOf(profileBuffer, (byte)0);
        var length = nullIndex >= 0 ? nullIndex : profileBuffer.Length;
        return Encoding.UTF8.GetString(profileBuffer, 0, length);
    }

    private void SetProfileBuffer(string value)
    {
        Array.Clear(profileBuffer);
        var bytes = Encoding.UTF8.GetBytes(value);
        Array.Copy(bytes, profileBuffer, Math.Min(bytes.Length, profileBuffer.Length - 1));
    }

    private enum StatusKind
    {
        Information,
        Success,
        Error,
    }

    private enum GuidedExportPhase
    {
        Idle,
        OpeningDresser,
        CapturingDresser,
        ClosingDresser,
        OpeningArmoire,
        CapturingArmoire,
        Complete,
        Paused,
        Cancelled,
    }
}
