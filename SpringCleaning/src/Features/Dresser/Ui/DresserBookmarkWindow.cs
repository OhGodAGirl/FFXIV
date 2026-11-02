using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using SpringCleaning.Shared.CollectionData;

namespace SpringCleaning;

internal sealed class DresserBookmarkWindow : Window, IDisposable
{
    private const ImGuiWindowFlags DresserBookmarkWindowFlags =
                ImGuiWindowFlags.AlwaysAutoResize
                | ImGuiWindowFlags.NoSavedSettings
                | ImGuiWindowFlags.NoDecoration
                | ImGuiWindowFlags.NoBackground
                | ImGuiWindowFlags.NoMove
                | ImGuiWindowFlags.NoNav
                | ImGuiWindowFlags.NoFocusOnAppearing;
    private const float TabWidth = 72f;
    private const float TabHeight = 74f;
    private const float AttachmentWidth = 10f;
    private const float FrameAttachmentOffset = 28f;
    private const float TabGap = 8f;
    private static readonly Vector4 Transparent = new(0f, 0f, 0f, 0f);
    private static readonly Vector4 Parchment = new(0.78f, 0.71f, 0.57f, 1f);
    private static readonly Vector4 ParchmentHovered = new(0.88f, 0.81f, 0.66f, 1f);
    private static readonly Vector4 ParchmentPressed = new(0.70f, 0.62f, 0.48f, 1f);
    private static readonly Vector4 Bronze = new(0.48f, 0.36f, 0.21f, 1f);
    private static readonly Vector4 Highlight = new(0.96f, 0.90f, 0.75f, 0.72f);
    private readonly MigrationController controller;
    private readonly MigrationWindow migrationWindow;
    private readonly SettingsWindow settingsWindow;
    private readonly DiagnosticWindow diagnosticWindow;
    private readonly DresserArmoireOverlayWindow dresserArmoireOverlayWindow;
    private readonly Configuration configuration;
    private readonly NativeUi nativeUi;
    private readonly NativeWindowFocusTracker nativeWindowFocusTracker;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private IReadOnlyList<NativeWindowOcclusionPolicy.Region> nativeOcclusions = [];
    private Vector2? creationOriginalPosition;
    private Vector2? creationAppliedPosition;
    private nint shiftedCreationAddress;
    private bool stylesPushed;
    private (bool Enabled, string Tooltip)? inventoryPresentation;
    private (bool Enabled, string Tooltip)? armouryPresentation;
    private (bool Enabled, string Tooltip)? migrationPresentation;

    public DresserBookmarkWindow(
                MigrationController controller,
                MigrationWindow migrationWindow,
                SettingsWindow settingsWindow,
                DiagnosticWindow diagnosticWindow,
                DresserArmoireOverlayWindow dresserArmoireOverlayWindow,
                Configuration configuration,
                NativeUi nativeUi,
                NativeWindowFocusTracker nativeWindowFocusTracker,
                IDalamudPluginInterface pluginInterface,
                IPluginLog log)
                : base(
                    "Spring Cleaning Dresser Store##SpringCleaningDresserStore",
                    DresserBookmarkWindowFlags)
    {
        this.controller = controller;
        this.migrationWindow = migrationWindow;
        this.settingsWindow = settingsWindow;
        this.diagnosticWindow = diagnosticWindow;
        this.dresserArmoireOverlayWindow = dresserArmoireOverlayWindow;
        this.configuration = configuration;
        this.nativeUi = nativeUi;
        this.nativeWindowFocusTracker = nativeWindowFocusTracker;
        this.pluginInterface = pluginInterface;
        this.log = log;
    }

    public unsafe void Dispose()
    {
        var address = nativeUi.IsGlamourCreationOpen ? nativeUi.GetGlamourCreationAddon() : 0;
        if (address == 0 || address != shiftedCreationAddress || creationOriginalPosition is not { } original)
            return;
        var creation = (AtkUnitBase*)address;
        if (new Vector2(creation->X, creation->Y) == creationAppliedPosition)
            _ = nativeUi.AlignGlamourCreation(original.X, original.Y);
        creationOriginalPosition = null;
        creationAppliedPosition = null;
    }

    public void BeginDresserSession()
    {
        ClearPresentation();
    }

    public void ClearPresentation()
    {
        inventoryPresentation = null;
        armouryPresentation = null;
        migrationPresentation = null;
    }

    public override unsafe void PreDraw()
    {
        Flags = DresserBookmarkWindowFlags;
        nativeOcclusions = [];
        try
        {
            var dresserAddress = nativeUi.GetDresserAddon();
            if (dresserAddress == 0)
            {
                migrationWindow.ReleaseDresserDock();
                IsOpen = false;
                return;
            }

            var dresser = (AtkUnitBase*)dresserAddress;
            var linkedOpacity = Math.Clamp(
                ImGui.GetStyle().Alpha * NativeWindowOcclusionPolicy.GetOpacity(
                    dresser->Alpha,
                    dresser->VisibilityState.HasFlag(
                        AtkUnitBaseVisibilityState.Hide),
                    dresser->AppliedVisibilityState.HasFlag(
                        AtkUnitBaseVisibilityState.Hide)),
                0f,
                1f);
            if (linkedOpacity <= 0f)
            {
                migrationWindow.ReleaseDresserDock();
                migrationWindow.SetNativeOcclusion(true);
                IsOpen = false;
                return;
            }

            var viewport = ImGui.GetMainViewport();
            var dresserWidth = dresser->GetScaledWidth(true);
            var dresserHeight = dresser->GetScaledHeight(true);
            var diagnosticWidth = WindowPlacementPolicy.GetScaledPanelWidth(
                DiagnosticWindow.PanelWidth,
                ImGuiHelpers.GlobalScale);
            var settingsWidth = WindowPlacementPolicy.GetScaledPanelWidth(
                SettingsWindow.PanelWidth,
                ImGuiHelpers.GlobalScale);
            var tabX = MathF.Max(
                viewport.WorkPos.X + 2f,
                dresser->X - TabWidth - AttachmentWidth + FrameAttachmentOffset + 2f);
            var railPosition = new Vector2(tabX, dresser->Y + 54f);
            ImGui.SetNextWindowPos(railPosition, ImGuiCond.Always);
            var railSize = new Vector2(
                TabWidth + AttachmentWidth,
                (TabHeight * 5f) + (TabGap * 4f));
            nativeOcclusions = NativeWindowOcclusion.GetAbove(
                dresser,
                new NativeWindowOcclusionPolicy.Region(
                    railPosition,
                    railPosition + railSize),
                nativeWindowFocusTracker.IsFocused(dresser));
            if (nativeOcclusions.Any(region =>
                    NativeWindowOcclusionPolicy.Contains(region, ImGui.GetMousePos())))
            {
                Flags |= ImGuiWindowFlags.NoInputs;
            }

            var anchor = new Vector2(dresser->X, dresser->Y);
            var creationAddress = nativeUi.IsGlamourCreationOpen ? nativeUi.GetGlamourCreationAddon() : 0;
            var creationSize = creationAddress == 0 ? Vector2.Zero : new Vector2(
                ((AtkUnitBase*)creationAddress)->GetScaledWidth(true),
                ((AtkUnitBase*)creationAddress)->GetScaledHeight(true));
            var layout = DrawerPlacementPolicy.Plan(anchor, new Vector2(dresserWidth, dresserHeight),
                viewport.WorkPos, viewport.WorkSize, creationSize, ImGuiHelpers.GlobalScale);
            var dresserFrame = dresser->WindowNode != null
                ? dresser->WindowNode->GetAsAtkComponentWindow() : null;
            var panelBounds = dresserFrame != null ? dresserFrame->UldManager.SearchNodeById(9) : null;
            if (layout.FitsBeside && panelBounds != null)
            {
                // Screenshot-calibrated insets align to the adjacent straight frame edge,
                // excluding the parchment frame's downward-curving middle and shadow.
                layout = layout with
                {
                    Position = new Vector2(layout.Position.X, panelBounds->ScreenY + 2f * dresser->Scale),
                    Size = new Vector2(layout.Size.X, (panelBounds->Height - 18f) * dresser->Scale),
                };
            }
            migrationWindow.MatchDresser(layout.Position, layout.Size);
            if (creationAddress != 0)
            {
                var creation = (AtkUnitBase*)creationAddress;
                var currentPosition = new Vector2(creation->X, creation->Y);
                if (shiftedCreationAddress != creationAddress)
                {
                    creationOriginalPosition = null;
                    creationAppliedPosition = null;
                    shiftedCreationAddress = creationAddress;
                }
                // The closed layout is derived from the Dresser, not a possibly persisted
                // expanded position from a previous plugin reload.
                var closedPosition = new Vector2(anchor.X + dresserWidth + 8f * ImGuiHelpers.GlobalScale, anchor.Y);
                closedPosition = DrawerPlacementPolicy.ClampPosition(closedPosition, creationSize,
                    viewport.WorkPos, viewport.WorkSize);
                var targetPosition = migrationWindow.IsOpen && layout.FitsBeside
                    ? layout.CreationPosition : closedPosition;
                creationOriginalPosition = closedPosition;
                if (currentPosition != targetPosition)
                    _ = nativeUi.AlignGlamourCreation(targetPosition.X, targetPosition.Y);
                creationAppliedPosition = new Vector2(MathF.Round(targetPosition.X), MathF.Round(targetPosition.Y));

            }

            // Evaluate after moving the companion: its previous bounds must not hide the drawer.
            var drawerBounds = configuration.RememberWindowPlacement && migrationWindow.DrawBounds is { } bounds
                ? bounds : new NativeWindowOcclusionPolicy.Region(layout.Position, layout.Position + layout.Size);
            var drawerOcclusions = NativeWindowOcclusion.GetAbove(dresser, drawerBounds,
                nativeWindowFocusTracker.IsFocused(dresser));
            migrationWindow.SetNativeOcclusion(
                NativeWindowOcclusionPolicy.GetVisibleRegions(drawerBounds, drawerOcclusions).Count == 0,
                drawerOcclusions.Any(region => NativeWindowOcclusionPolicy.Contains(region, ImGui.GetMousePos())));

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
            ImGui.PushStyleVar(
                ImGuiStyleVar.Alpha,
                linkedOpacity);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, Transparent);
            stylesPushed = true;
        }
        catch (Exception ex)
        {
            log.Debug(ex, "Could not anchor the inventory-to-Dresser tab.");
            migrationWindow.ReleaseDresserDock();
            IsOpen = false;
        }
    }

    public override void PostDraw()
    {
        if (!stylesPushed)
            return;

        ImGui.PopStyleColor();
        ImGui.PopStyleVar(4);
        stylesPushed = false;
    }

    public UiDrawObservation DrawObservation { get; private set; }

    public override void Draw()
    {
        DrawObservation = UiDrawObservation.Capture();
        DrawStoreTab();
        ImGui.Dummy(new Vector2(0f, TabGap));
        DrawArmouryStoreTab();
        ImGui.Dummy(new Vector2(0f, TabGap));
        DrawDresserToArmoireTab();
        ImGui.Dummy(new Vector2(0f, TabGap));
        DrawDuplicatesTab();
        ImGui.Dummy(new Vector2(0f, TabGap));
        DrawSettingsTab();
    }

    private enum GlamourCreationDockTarget
    {
        None,
        Dresser,
        Migration,
        Settings,
        Diagnostics,
    }

    private void DrawDresserToArmoireTab()
    {
        var canStart = controller.CanStart && !migrationWindow.IsCleanupRunning;
        if (!controller.RetainPresentationDuringRefresh || migrationPresentation is null)
            migrationPresentation = (canStart, controller.HasFreshDresserArmoireOwnership
                ? "Glamour Dresser to Armoire"
                : controller.OwnershipReadiness(CollectionSourceName.GlamourDresser, CollectionSourceName.Armoire));
        var clicked = ImGui.InvisibleButton("##DresserToArmoireView",
            new Vector2(TabWidth + AttachmentWidth, TabHeight));
        var hovered = ImGui.IsItemHovered();
        DrawTabSurface(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), migrationPresentation.Value.Enabled,
            hovered, ImGui.IsItemActive(), false, SpringCleaningUi.BookmarkGlyph.DresserToArmoire);
        if (hovered)
            ImGui.SetTooltip(migrationPresentation.Value.Tooltip);
        if (clicked && canStart)
            controller.Start();
    }

    private void DrawArmouryStoreTab()
    {
        var canStart = controller.CanStartArmouryDresser && !migrationWindow.IsCleanupRunning;
        if (!controller.RetainPresentationDuringRefresh || armouryPresentation is null)
            armouryPresentation = (canStart, controller.HasFreshArmouryDresserOwnership
                ? "Armoury Chest to Glamour Dresser"
                : controller.OwnershipReadiness(CollectionSourceName.ArmoryChest, CollectionSourceName.GlamourDresser));
        var clicked = ImGui.InvisibleButton(
            "##StoreArmouryInDresser",
            new Vector2(TabWidth + AttachmentWidth, TabHeight));
        var minimum = ImGui.GetItemRectMin();
        var maximum = ImGui.GetItemRectMax();
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();

        DrawTabSurface(
            minimum,
            maximum,
            armouryPresentation.Value.Enabled,
            hovered,
            held,
            false,
            SpringCleaningUi.BookmarkGlyph.ArmouryStore);
        if (hovered)
        {
            ImGui.SetTooltip(armouryPresentation.Value.Tooltip);
        }

        if (clicked && canStart)
            controller.StartArmouryDresserStore();
    }

    private void DrawStoreTab()
    {
        var canStart = controller.CanStartInventoryDresser && !migrationWindow.IsCleanupRunning;
        if (!controller.RetainPresentationDuringRefresh || inventoryPresentation is null)
            inventoryPresentation = (canStart, controller.HasFreshInventoryDresserOwnership
                ? "Inventory to Glamour Dresser"
                : controller.OwnershipReadiness(CollectionSourceName.Inventory, CollectionSourceName.GlamourDresser));
        var clicked = ImGui.InvisibleButton(
            "##StoreInventoryInDresser",
            new Vector2(TabWidth + AttachmentWidth, TabHeight));
        var minimum = ImGui.GetItemRectMin();
        var maximum = ImGui.GetItemRectMax();
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();

        DrawTabSurface(minimum, maximum, inventoryPresentation.Value.Enabled, hovered, held, false, SpringCleaningUi.BookmarkGlyph.Store);
        if (hovered)
        {
            ImGui.SetTooltip(inventoryPresentation.Value.Tooltip);
        }

        if (clicked && canStart)
            controller.StartInventoryDresserStore();
    }

    private void DrawDuplicatesTab()
    {
        var clicked = ImGui.InvisibleButton(
            "##SpringCleaningDuplicates",
            new Vector2(TabWidth + AttachmentWidth, TabHeight));
        var minimum = ImGui.GetItemRectMin();
        var maximum = ImGui.GetItemRectMax();
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();
        var selected = migrationWindow.IsOpen
            && migrationWindow.ActivePage == SpringCleaningPage.Duplicates;

        DrawTabSurface(minimum, maximum, true, hovered, held, selected, SpringCleaningUi.BookmarkGlyph.Duplicates);
        if (hovered)
            ImGui.SetTooltip("Duplicates");
        if (clicked)
        {
            if (selected)
                migrationWindow.IsOpen = false;
            else
                migrationWindow.OpenPage(SpringCleaningPage.Duplicates);
        }
    }

    private void DrawSettingsTab()
    {
        var canOpen = !controller.IsActive && !migrationWindow.IsCleanupRunning;
        var clicked = ImGui.InvisibleButton("##SpringCleaningSettings",
            new Vector2(TabWidth + AttachmentWidth, TabHeight));
        var hovered = ImGui.IsItemHovered();
        var selected = migrationWindow.IsOpen && migrationWindow.ActivePage == SpringCleaningPage.Settings;
        DrawTabSurface(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), canOpen, hovered,
            ImGui.IsItemActive(), selected, SpringCleaningUi.BookmarkGlyph.Settings);
        if (hovered)
            ImGui.SetTooltip("Settings");
        if (clicked && canOpen)
        {
            settingsWindow.IsOpen = false;
            if (selected)
                migrationWindow.IsOpen = false;
            else
                migrationWindow.OpenPage(SpringCleaningPage.Settings);
        }
    }

    private void DrawTabSurface(
        Vector2 itemMinimum,
        Vector2 itemMaximum,
        bool enabled,
        bool hovered,
        bool held,
        bool selected,
        SpringCleaningUi.BookmarkGlyph glyph)
    {
        DrawOutsideNativeOcclusions(
            itemMinimum,
            itemMaximum,
            () => DrawTabSurfaceUnclipped(
                itemMinimum,
                itemMaximum,
                enabled,
                hovered,
                held,
                selected,
                glyph));
    }

    private void DrawTabSurfaceUnclipped(
        Vector2 itemMinimum,
        Vector2 itemMaximum,
        bool enabled,
        bool hovered,
        bool held,
        bool selected,
        SpringCleaningUi.BookmarkGlyph glyph)
    {
        var drawList = ImGui.GetWindowDrawList();
        var fill = held || selected
            ? ParchmentPressed
            : hovered && enabled
                ? ParchmentHovered
                : Parchment;
        if (!enabled)
            fill = new Vector4(fill.X, fill.Y, fill.Z, 0.68f);

        var bodyMinimum = itemMinimum;
        var bodyMaximum = itemMaximum;
        SpringCleaningUi.DrawBookmarkTab(drawList, bodyMinimum, bodyMaximum,
            ImGui.GetColorU32(fill), ImGui.GetColorU32(Bronze),
            (bodyMaximum.Y - bodyMinimum.Y) / 48f, configuration.TabStyle);

        var center = (bodyMinimum + bodyMaximum) * 0.5f;
        // Draw in a fixed design space, then scale to the actual tab bounds.
        var glyphScale = MathF.Min(bodyMaximum.X - bodyMinimum.X, bodyMaximum.Y - bodyMinimum.Y) / 64f;
        SpringCleaningUi.DrawSourceFirstGlyph(drawList, center, glyphScale, enabled, glyph);
    }

    private void DrawOutsideNativeOcclusions(
        Vector2 itemMinimum,
        Vector2 itemMaximum,
        Action draw)
    {
        var regions = NativeWindowOcclusionPolicy.GetVisibleRegions(
            new NativeWindowOcclusionPolicy.Region(itemMinimum, itemMaximum),
            nativeOcclusions);
        var drawList = ImGui.GetWindowDrawList();
        foreach (var region in regions)
        {
            drawList.PushClipRect(region.Minimum, region.Maximum, true);
            draw();
            drawList.PopClipRect();
        }
    }

    private static Vector4 WithEnabledAlpha(Vector4 color, bool enabled)
    {
        return enabled ? color : new Vector4(color.X, color.Y, color.Z, 0.52f);
    }
}
