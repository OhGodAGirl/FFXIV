using System.Numerics;
using Dalamud.Game.Config;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using SpringCleaning.Shared.CollectionData;

namespace SpringCleaning;

internal sealed class QuickStoreWindow : Window, IDisposable
{
    private const ImGuiWindowFlags QuickStoreWindowFlags =
                ImGuiWindowFlags.AlwaysAutoResize
                | ImGuiWindowFlags.NoSavedSettings
                | ImGuiWindowFlags.NoDecoration
                | ImGuiWindowFlags.NoBackground
                | ImGuiWindowFlags.NoMove
                | ImGuiWindowFlags.NoNav
                | ImGuiWindowFlags.NoFocusOnAppearing;
    private const float TabWidth = 100f;
    private const float TabHeight = 86f;
    private const float AttachmentWidth = 11f;
    private const float FrameGap = 0f;
    private const float TabGap = 8f;
    private static readonly Vector4 Transparent = new(0f, 0f, 0f, 0f);
    private static readonly Vector4 Tab = new(0.27f, 0.275f, 0.27f, 0.99f);
    private static readonly Vector4 TabHovered = new(0.34f, 0.345f, 0.33f, 1f);
    private static readonly Vector4 GoldBorder = new(0.72f, 0.62f, 0.42f, 1f);
    private static readonly Vector4 Icon = new(0.91f, 0.76f, 0.43f, 1f);
    private static readonly Vector4 IconMuted = new(0.62f, 0.61f, 0.57f, 1f);
    private readonly ArmoireThemeState themeState = new();
    private Func<uint?>? readTheme;

    internal void UpdateTheme(IGameConfig gameConfig) =>
        themeState.Update(Environment.TickCount64,
            readTheme ??= () => gameConfig.TryGet(SystemConfigOption.ColorThemeType, out uint theme) ? theme : null);

    private readonly Configuration configuration;
    private readonly MigrationController controller;
    private readonly SettingsWindow settingsWindow;
    private readonly NativeUi nativeUi;
    private readonly NativeWindowFocusTracker nativeWindowFocusTracker;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private IReadOnlyList<NativeWindowOcclusionPolicy.Region> nativeOcclusions = [];
    private bool stylesPushed;
    private (bool Enabled, string Tooltip)? storePresentation;

    public QuickStoreWindow(
                Configuration configuration,
                MigrationController controller,
                SettingsWindow settingsWindow,
                NativeUi nativeUi,
                NativeWindowFocusTracker nativeWindowFocusTracker,
                IDalamudPluginInterface pluginInterface,
                IPluginLog log)
                : base(
                    "Spring Cleaning Quick Store##SpringCleaningQuickStore",
                    QuickStoreWindowFlags)
    {
        this.configuration = configuration;
        this.controller = controller;
        this.settingsWindow = settingsWindow;
        this.nativeUi = nativeUi;
        this.nativeWindowFocusTracker = nativeWindowFocusTracker;
        this.pluginInterface = pluginInterface;
        this.log = log;
    }

    public void Dispose()
    {
    }

    public void ClearPresentation() => storePresentation = null;

    public override unsafe void PreDraw()
    {
        Flags = QuickStoreWindowFlags;
        nativeOcclusions = [];
        var linkedOpacity = ImGui.GetStyle().Alpha;
        try
        {
            var address = nativeUi.GetArmoireAddon();
            if (address == 0)
            {
                IsOpen = false;
                return;
            }

            var addon = (AtkUnitBase*)address;
            linkedOpacity *= NativeWindowOcclusionPolicy.GetOpacity(
                addon->Alpha,
                addon->VisibilityState.HasFlag(
                    AtkUnitBaseVisibilityState.Hide),
                addon->AppliedVisibilityState.HasFlag(
                    AtkUnitBaseVisibilityState.Hide));
            if (linkedOpacity <= 0f)
            {
                IsOpen = false;
                return;
            }

            var frame = addon->WindowNode;
            var windowComponent = frame != null ? frame->GetAsAtkComponentWindow() : null;
            var visibleFrame = windowComponent != null ? windowComponent->UldManager.SearchNodeById(9) : null;
            // Anchor to the visible frame, not the addon origin's transparent margin.
            var frameLeft = visibleFrame != null ? visibleFrame->ScreenX : addon->X;
            var totalWidth = TabWidth + AttachmentWidth;
            var railPosition = new Vector2(
                ArmoireBookmarkPlacementPolicy.GetLeft(
                    frameLeft,
                    totalWidth,
                    FrameGap),
                addon->Y + 52f);
            ImGui.SetNextWindowPos(
                railPosition,
                ImGuiCond.Always);
            var railSize = new Vector2(totalWidth, (TabHeight * 2f) + TabGap);
            nativeOcclusions = NativeWindowOcclusion.GetAbove(
                addon,
                new NativeWindowOcclusionPolicy.Region(
                    railPosition,
                    railPosition + railSize),
                nativeWindowFocusTracker.IsFocused(addon));
            if (nativeOcclusions.Any(region =>
                    NativeWindowOcclusionPolicy.Contains(region, ImGui.GetMousePos())))
            {
                Flags |= ImGuiWindowFlags.NoInputs;
            }

            var viewport = ImGui.GetMainViewport();
            var armoireRight = addon->X + addon->GetScaledWidth(true);
            // The addon bounds include padding outside the visible window component.
            // Native window image 9 excludes the measured outer shadow margins.
            var frameTop = visibleFrame != null ? visibleFrame->ScreenY : addon->Y;
            var frameHeight = visibleFrame != null ? visibleFrame->Height * addon->Scale : addon->GetScaledHeight(true);
            var settingsSize = new Vector2(SettingsWindow.PanelWidth * ImGuiHelpers.GlobalScale, frameHeight);
            var settingsPosition = DrawerPlacementPolicy.ClampPosition(
                new Vector2(armoireRight + 8f * ImGuiHelpers.GlobalScale, addon->Y),
                settingsSize,
                viewport.WorkPos,
                viewport.WorkSize);
            settingsPosition.Y = frameTop;
            settingsWindow.MatchArmoire(settingsPosition, settingsSize);

        }
        catch (Exception ex)
        {
            log.Debug(ex, "Could not anchor the quick-store tab to the Armoire.");
            IsOpen = false;
            return;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, linkedOpacity);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Transparent);
        stylesPushed = true;
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
        DrawSettingsTab();
    }

    private void DrawStoreTab()
    {
        var canStart = controller.CanStartInventoryArmoire;
        if (!controller.RetainPresentationDuringRefresh || storePresentation is null)
            storePresentation = (canStart, canStart
                ? "Inventory to Armoire"
                : controller.IsActive ? controller.Status
                : !controller.HasFreshInventoryArmoireOwnership
                    ? controller.OwnershipReadiness(CollectionSourceName.Inventory, CollectionSourceName.Armoire)
                    : "Waiting for the game interface to finish restoring.");
        var clicked = ImGui.InvisibleButton(
            "##StoreInventoryInArmoire",
            new Vector2(TabWidth + AttachmentWidth, TabHeight));
        var minimum = ImGui.GetItemRectMin();
        var maximum = ImGui.GetItemRectMax();
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();

        DrawTabSurface(minimum, maximum, storePresentation.Value.Enabled, hovered, held, false, false);
        if (hovered)
        {
            ImGui.SetTooltip(storePresentation.Value.Tooltip);
        }

        if (clicked && canStart)
            controller.StartInventoryArmoireStore();
    }

    private void DrawSettingsTab()
    {
        var clicked = ImGui.InvisibleButton(
            "##SpringCleaningArmoireSettings",
            new Vector2(TabWidth + AttachmentWidth, TabHeight));
        var minimum = ImGui.GetItemRectMin();
        var maximum = ImGui.GetItemRectMax();
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();

        DrawTabSurface(
            minimum,
            maximum,
            true,
            hovered,
            held,
            settingsWindow.IsOpen,
            true);
        if (hovered)
            ImGui.SetTooltip("Settings");
        if (clicked)
            settingsWindow.Toggle();
    }

    private void DrawTabSurface(
        Vector2 itemMinimum,
        Vector2 itemMaximum,
        bool enabled,
        bool hovered,
        bool held,
        bool selected,
        bool settings)
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
                settings));
    }

    private void DrawTabSurfaceUnclipped(
        Vector2 itemMinimum,
        Vector2 itemMaximum,
        bool enabled,
        bool hovered,
        bool held,
        bool selected,
        bool settings)
    {
        var drawList = ImGui.GetWindowDrawList();
        var offset = enabled ? held || selected ? 0.08f : hovered ? 0.045f : 0f : 0f;
        if (themeState.Light)
            offset = -offset;
        var fill = themeState.Surface + new Vector4(offset, offset, offset, 0f);
        var border = themeState.Light ? new Vector4(0.48f, 0.40f, 0.27f, 1f) : GoldBorder;
        var ink = themeState.Light ? new Vector4(0.41f, 0.28f, 0.045f, 1f) : Icon;
        var inset = themeState.Surface - new Vector4(0.04f, 0.04f, 0.04f, 0f);
        if (!enabled)
            fill = new Vector4(fill.X, fill.Y, fill.Z, 0.72f);

        var bodyMinimum = itemMinimum;
        var bodyMaximum = itemMaximum;
        SpringCleaningUi.DrawBookmarkTab(drawList, bodyMinimum, bodyMaximum,
            ImGui.GetColorU32(fill), ImGui.GetColorU32(border),
            (bodyMaximum.Y - bodyMinimum.Y) / 48f, configuration.TabStyle, inset);

        var center = (bodyMinimum + bodyMaximum) * 0.5f;
        var glyphScale = MathF.Min(bodyMaximum.X - bodyMinimum.X, bodyMaximum.Y - bodyMinimum.Y) / 64f;
        SpringCleaningUi.DrawSourceFirstGlyph(drawList, center, glyphScale, enabled,
            settings ? SpringCleaningUi.BookmarkGlyph.Settings : SpringCleaningUi.BookmarkGlyph.Store, darkSurface: true, inkColor: ink, paperColor: fill);
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
        return enabled ? color : new Vector4(color.X, color.Y, color.Z, 0.55f);
    }
}
