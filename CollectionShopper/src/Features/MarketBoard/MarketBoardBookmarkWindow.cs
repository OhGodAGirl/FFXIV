using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace CollectionShopper;

internal sealed class MarketBoardBookmarkWindow : Window
{
    private const ImGuiWindowFlags BookmarkWindowFlags =
        ImGuiWindowFlags.AlwaysAutoResize
        | ImGuiWindowFlags.NoSavedSettings
        | ImGuiWindowFlags.NoDecoration
        | ImGuiWindowFlags.NoBackground
        | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoNav
        | ImGuiWindowFlags.NoFocusOnAppearing;
    private const int MaximumNativeOccluders = 32;
    private MarketBoardTabPresentation.TabPalette palette = MarketBoardTabPresentation.ResolveTheme(0);
    internal void SetGameTheme(uint theme) => palette = MarketBoardTabPresentation.ResolveTheme(theme);
    private Vector4 Tab => palette.Surface;
    private Vector4 TabHovered => palette.Hover;
    private Vector4 TabSelected => palette.Active;
    private Vector4 GoldBorder => palette.Border;
    private Vector4 Icon => palette.Icon;
    private Vector4 Badge => palette.Inset;

    private static readonly HashSet<string> NonWindowOccluderNames = new(StringComparer.Ordinal)
    {
        "Inventory",
        "InventoryLarge",
        "InventoryExpansion",
        "InventoryBuddy",
        "_Tooltip",
        "_ItemDetail",
        "ItemDetail",
        "_ActionDetail",
    };

    private static readonly Vector4 Transparent = new(0f, 0f, 0f, 0f);

    private readonly ShopperWindow shopper;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IGameGui gameGui;
    private readonly IPluginLog log;
    private readonly List<MarketBoardTabPresentation.ClipRegion> nativeOcclusions =
        new(MaximumNativeOccluders);
    private readonly List<MarketBoardTabPresentation.ClipRegion> nativeInputOcclusions = new(MaximumNativeOccluders);
    private readonly Dictionary<nint, (uint DepthLayer, int LayerIndex)> nativeOrder = new(256);
    private readonly List<MarketBoardTabPresentation.NativeWindowSnapshot> nativeWindows = new(256);
    private readonly List<MarketBoardTabPresentation.NativeWindowSnapshot> nativeInputWindows = new(256);
    // Recomputed for each tab; capacity is retained for this window's lifetime.
    // The native snapshot admits at most 32 rectangular occluders.
    private readonly List<MarketBoardTabPresentation.ClipRegion> visibleTabRegions = new(4);
    private readonly List<MarketBoardTabPresentation.ClipRegion> tabRegionScratch = new(4);
    private Vector2 shopperAnchor;
    private Vector2 railPosition;
    private float tabScale = 1f;
    private float opacity;
    private bool nativeOrderingAvailable;
    private bool stylesPushed;
    private TabInputSnapshot? lastTabInput;
    private TabInputSnapshot? lastTabClick;

    // Two value snapshots survive closing the board so its diagnostics remain
    // available even when another input owner prevents copying while it is open.
    private readonly record struct TabInputSnapshot(DateTime AtUtc, ShopperPage Page,
        bool NativeBlocked, bool ItemHovered, bool RelaxedHovered, bool WindowHovered,
        bool Active, bool Pressed, bool Released, bool Clicked, bool CaptureMouse,
        ImGuiWindowFlags WindowFlags, int Occluders);

    internal DiagnosticCheck CaptureTabInputDiagnostics(bool click)
    {
        var snapshot = click ? lastTabClick : lastTabInput;
        var name = click ? "Last tab click input" : "Last tab hover input";
        if (snapshot is not { } value)
            return new(name, DiagnosticResultKind.NotAvailable, "No tab input snapshot captured.");
        return new(name, DiagnosticResultKind.Pass,
            $"{value.AtUtc:O}; page={value.Page}; nativeBlocked={value.NativeBlocked}; "
            + $"hover={value.ItemHovered}/{value.RelaxedHovered}; windowHover={value.WindowHovered}; "
            + $"active={value.Active}; down/up/click={value.Pressed}/{value.Released}/{value.Clicked}; "
            + $"capture={value.CaptureMouse}; flags={value.WindowFlags}; occluders={value.Occluders}");
    }
    private int motionTraceFrames;
    private long motionTraceStarted;
    private Vector2 motionNativePosition;
    private Vector2 motionBoundsPosition;
    private Vector2 motionBoundsMin;
    private Vector2 motionBoundsMax;
    private Vector2 motionPlacementMin;
    private Vector2 motionPlacementMax;
    private int motionNativeDraws;
    private int motionPairedSamples;
    private Vector2 motionLastDrawPosition;
    private Vector2 motionDrawOffsetMin;
    private Vector2 motionDrawOffsetMax;

    internal unsafe void ObserveNativeDraw(AddonEvent type, AddonArgs args)
    {
        if (motionTraceStarted == 0 || args.Addon.IsNull)
            return;
        var addon = (AtkUnitBase*)args.Addon.Address;
        GetNativeWindowGeometry(addon, out motionLastDrawPosition, out _);
        motionNativeDraws++;
    }

    internal void StartMotionTrace()
    {
        motionTraceFrames = 0;
        motionNativeDraws = motionPairedSamples = 0;
        motionDrawOffsetMin = new(float.PositiveInfinity);
        motionDrawOffsetMax = new(float.NegativeInfinity);
        motionTraceStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        motionBoundsMin = motionPlacementMin = new(float.PositiveInfinity);
        motionBoundsMax = motionPlacementMax = new(float.NegativeInfinity);
        log.Warning("Motion trace started: move Item Search for 15 seconds. Only window coordinate differences are recorded.");
    }

    private void ObserveMotionTrace()
    {
        if (motionTraceStarted == 0)
            return;
        var boundsOffset = motionBoundsPosition - motionNativePosition;
        var placementOffset = ImGui.GetWindowPos() - railPosition;
        if (motionNativeDraws > 0)
        {
            var drawOffset = motionBoundsPosition - motionLastDrawPosition;
            motionDrawOffsetMin = Vector2.Min(motionDrawOffsetMin, drawOffset);
            motionDrawOffsetMax = Vector2.Max(motionDrawOffsetMax, drawOffset);
            motionPairedSamples++;
        }
        motionBoundsMin = Vector2.Min(motionBoundsMin, boundsOffset);
        motionBoundsMax = Vector2.Max(motionBoundsMax, boundsOffset);
        motionPlacementMin = Vector2.Min(motionPlacementMin, placementOffset);
        motionPlacementMax = Vector2.Max(motionPlacementMax, placementOffset);
        motionTraceFrames++;
        if (System.Diagnostics.Stopwatch.GetElapsedTime(motionTraceStarted).TotalSeconds < 15 && motionTraceFrames < 10000)
            return;
        motionTraceStarted = 0;
        log.Warning("Motion trace complete: {Frames} frames; bounds-minus-addon min {BoundsMin}, max {BoundsMax}; drawn-minus-requested rail min {RailMin}, max {RailMax}.",
            motionTraceFrames, motionBoundsMin, motionBoundsMax, motionPlacementMin, motionPlacementMax);
        log.Warning("Motion draw-phase trace: {Draws} native draws, {Pairs} paired overlay samples; overlay-minus-last-native-draw min {Min}, max {Max}. No pairs means unavailable.",
            motionNativeDraws, motionPairedSamples,
            motionPairedSamples > 0 ? motionDrawOffsetMin : Vector2.Zero,
            motionPairedSamples > 0 ? motionDrawOffsetMax : Vector2.Zero);
    }

    public MarketBoardBookmarkWindow(
        ShopperWindow shopper,
        IDalamudPluginInterface pluginInterface,
        IGameGui gameGui,
        IPluginLog log)
        : base(
            "Collection Shopper Market Board Bookmarks##CollectionShopperBookmarks",
            BookmarkWindowFlags)
    {
        this.shopper = shopper;
        this.pluginInterface = pluginInterface;
        this.gameGui = gameGui;
        this.log = log;
        // Native alpha owns visibility; replayed host fades can retain geometry
        // at the previous anchor position after an unavailable native frame.
        DisableFadeInFadeOut = true;
    }

    // Both ImGui windows consume this frame's snapshot, even though the shopper
    // must draw before the bookmark rail to preserve existing input ownership.
    public unsafe void PrepareNativeFrame()
    {
        nativeOrderingAvailable = false;
        shopper.SetNativeOcclusionUnavailable();
        nativeOcclusions.Clear();
        nativeInputOcclusions.Clear();
        nativeOrder.Clear();
        nativeWindows.Clear();
        nativeInputWindows.Clear();
        try
        {
            var search = gameGui.GetAddonByName("ItemSearch");
            var results = gameGui.GetAddonByName("ItemSearchResult");
            var retainerSell = gameGui.GetAddonByName("RetainerSell");
            if (!MarketBoardContext.IsBrowsing(
                    !search.IsNull && search.IsVisible,
                    !results.IsNull && results.IsVisible,
                    !retainerSell.IsNull && retainerSell.IsVisible))
            {
                shopper.ClearMarketBoardAttachment();
                IsOpen = false;
                return;
            }

            var anchor = !search.IsNull && search.IsVisible ? search : results;

            var addon = (AtkUnitBase*)anchor.Address;
            if (addon->RootNode == null)
            {
                IsOpen = false;
                return;
            }
            var hiddenByCurrentVisibility = addon->VisibilityState.HasFlag(
                AtkUnitBaseVisibilityState.Hide);
            var hiddenByAppliedVisibility = addon->AppliedVisibilityState.HasFlag(
                AtkUnitBaseVisibilityState.Hide);
            opacity = MarketBoardTabPresentation.GetOpacity(
                addon->Alpha,
                hiddenByCurrentVisibility,
                hiddenByAppliedVisibility);
            if (opacity <= 0f)
            {
                IsOpen = false;
                return;
            }

            var viewport = ImGui.GetMainViewport();
            GetNativeWindowGeometry(addon, out var marketPosition, out var marketSize);
            motionNativePosition = new(addon->X, addon->Y);
            motionBoundsPosition = marketPosition;
            tabScale = ImGuiHelpers.GlobalScale;
            GetNativeOcclusionGeometry(addon, out var visibleFramePosition, out var visibleFrameSize);

            var visibleTabCount = 2
                + (shopper.ShouldShowOutfitsBookmark ? 1 : 0)
                + (shopper.ShouldShowWeaponsBookmark ? 1 : 0)
                + (shopper.ShouldShowMinionsBookmark ? 1 : 0)
                + (shopper.ShouldShowMountsBookmark ? 1 : 0)
                + (shopper.ShouldShowOrchestrionsBookmark ? 1 : 0)
                + (shopper.ShouldShowHairstylesBookmark ? 1 : 0)
                + (shopper.ShouldShowFacewearBookmark ? 1 : 0)
                + (shopper.ShouldShowFashionBookmark ? 1 : 0);
            railPosition = MarketBoardTabPresentation.RailPosition(marketPosition, visibleFramePosition,
                visibleFrameSize.Y, viewport.WorkPos.X, visibleTabCount, tabScale);
            const float companionGap = 8f;
            var companionPlacement = MarketBoardTabPresentation.GetLockedCompanionPlacement(
                marketPosition,
                marketSize,
                companionGap);
            shopperAnchor = companionPlacement.Position;
            var shopperSize = companionPlacement.Size;
            if (!TryCaptureNativeOcclusions(addon))
            {
                IsOpen = false;
                return;
            }

            shopper.MatchMarketBoard(
                shopperAnchor,
                shopperSize,
                nativeOcclusions,
                nativeInputOcclusions);
            nativeOrderingAvailable = true;
            IsOpen = true;
        }
        catch (Exception ex)
        {
            log.Debug(ex, "Could not anchor Collection Shopper bookmarks to the Market Board.");
            IsOpen = false;
        }
    }

    // The caller must identify a click on this rail or the companion. This
    // adapter additionally rejects covered clicks and revalidates native state.
    internal unsafe void FocusMarketBoardFromUncoveredClick(bool surfaceHovered)
    {
        if (!MarketBoardTabPresentation.CanFocusFromClick(surfaceHovered,
                ImGui.IsMouseClicked(ImGuiMouseButton.Left),
                MarketBoardTabPresentation.IsPointOccluded(ImGui.GetMousePos(), nativeInputOcclusions),
                nativeOrderingAvailable))
            return;

        try
        {
            var search = gameGui.GetAddonByName("ItemSearch");
            var results = gameGui.GetAddonByName("ItemSearchResult");
            var retainerSell = gameGui.GetAddonByName("RetainerSell");
            if (!MarketBoardContext.IsBrowsing(
                    !search.IsNull && search.IsVisible,
                    !results.IsNull && results.IsVisible,
                    !retainerSell.IsNull && retainerSell.IsVisible))
                return;

            // Results can block Item Search. Focus the active native member and
            // let the game's existing HostId/group bookkeeping raise the group.
            var target = !results.IsNull && results.IsVisible ? results : search;
            var addon = (AtkUnitBase*)target.Address;
            if (!nativeOrder.TryGetValue(target.Address, out var order)
                || addon->DepthLayer != order.DepthLayer
                || addon->RootNode == null
                || !addon->RootNode->IsVisible()
                || addon->FocusNode == null
                || addon->DisableFocusability
                || addon->ShouldIgnoreInputs()
                || MarketBoardTabPresentation.GetOpacity(
                    addon->Alpha,
                    addon->VisibilityState.HasFlag(AtkUnitBaseVisibilityState.Hide),
                    addon->AppliedVisibilityState.HasFlag(AtkUnitBaseVisibilityState.Hide)) <= 0f)
                return;

            // AtkUnitBase.Focus uses the existing focus node and native focus
            // manager. It does not synthesize button events or addon callbacks.
            addon->Focus();
        }
        catch (Exception ex)
        {
            log.Debug(ex, "Could not focus the Market Board after a Collection Shopper click.");
        }
    }

    public override bool DrawConditions() => nativeOrderingAvailable;

    public override void PreDraw()
    {
        Flags = BookmarkWindowFlags;
        if (MarketBoardTabPresentation.IsPointOccluded(ImGui.GetMousePos(), nativeInputOcclusions))
            Flags |= ImGuiWindowFlags.NoInputs;

        ImGui.SetNextWindowPos(railPosition, ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1.5f);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, opacity);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Transparent);
        ImGui.PushStyleColor(ImGuiCol.Border, GoldBorder);
        stylesPushed = true;
    }

    public override void PostDraw()
    {
        if (!stylesPushed)
            return;

        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(7);
        stylesPushed = false;
    }

    public override void Draw()
    {
        ObserveMotionTrace();
        var drewTab = false;
        if (shopper.ShouldShowOutfitsBookmark)
        {
            DrawTab(ShopperPage.Outfits, FontAwesomeIcon.Tshirt, "Outfits", true, drewTab);
            drewTab = true;
        }

        if (shopper.ShouldShowWeaponsBookmark)
        {
            DrawTab(ShopperPage.Weapons, FontAwesomeIcon.Hammer, "Weapons", true, drewTab);
            drewTab = true;
        }

        if (shopper.ShouldShowMinionsBookmark)
        {
            DrawTab(
                ShopperPage.Minions,
                FontAwesomeIcon.Paw,
                "Minions",
                shopper.HasMissingMinions,
                drewTab);
            drewTab = true;
        }

        if (shopper.ShouldShowMountsBookmark)
        {
            DrawTab(
                ShopperPage.Mounts,
                FontAwesomeIcon.Horse,
                "Mounts",
                shopper.HasMissingMounts,
                drewTab);
            drewTab = true;
        }

        if (shopper.ShouldShowOrchestrionsBookmark)
        {
            DrawTab(
                ShopperPage.Orchestrions,
                FontAwesomeIcon.Music,
                "Orchestrion Rolls",
                shopper.HasMissingOrchestrions,
                drewTab);
            drewTab = true;
        }

        if (shopper.ShouldShowHairstylesBookmark)
        {
            DrawTab(
                ShopperPage.Hairstyles,
                FontAwesomeIcon.Cut,
                "Hairstyles",
                shopper.HasMissingHairstyles,
                drewTab);
            drewTab = true;
        }

        if (shopper.ShouldShowFacewearBookmark)
        {
            DrawTab(
                ShopperPage.Facewear,
                FontAwesomeIcon.Glasses,
                "Glasses",
                shopper.HasMissingFacewear,
                drewTab);
            drewTab = true;
        }

        if (shopper.ShouldShowFashionBookmark)
        {
            DrawTab(
                ShopperPage.Fashion,
                FontAwesomeIcon.Umbrella,
                "Fashion Accessories",
                shopper.HasMissingFashion,
                drewTab);
            drewTab = true;
        }

        DrawTab(ShopperPage.AllMissing, FontAwesomeIcon.ShoppingBag, "All Missing", true, drewTab);
        drewTab = true;

        DrawTab(ShopperPage.Settings, FontAwesomeIcon.Cog, "Settings", false, drewTab);
    }

    private void DrawTab(
        ShopperPage page,
        FontAwesomeIcon icon,
        string tooltip,
        bool showMissingBadge,
        bool addGap)
    {
        if (addGap)
            ImGui.Dummy(new Vector2(0f, MarketBoardTabPresentation.TabGap * tabScale));

        var selected = shopper.IsOpen && shopper.ActivePage == page;
        ImGui.SetCursorPosX(0f);
        var tabSize = new Vector2(
            MarketBoardTabPresentation.TabBodySize + MarketBoardTabPresentation.TabExtension,
            MarketBoardTabPresentation.TabBodySize) * tabScale;
        ImGui.PushID((int)page);
        var clicked = ImGui.InvisibleButton("##tab", tabSize);
        var itemMin = ImGui.GetItemRectMin();
        var itemMax = ImGui.GetItemRectMax();
        var mouseOccluded = MarketBoardTabPresentation.IsPointOccluded(
            ImGui.GetMousePos(),
            nativeInputOcclusions);
        var hovered = ImGui.IsItemHovered() && !mouseOccluded;
        var held = ImGui.IsItemActive() && !mouseOccluded;
        if (ImGui.IsMouseHoveringRect(itemMin, itemMax, false))
        {
            var pressed = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
            var released = ImGui.IsMouseReleased(ImGuiMouseButton.Left);
            lastTabInput = new(DateTime.UtcNow, page, mouseOccluded, ImGui.IsItemHovered(),
                ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem
                    | ImGuiHoveredFlags.AllowWhenBlockedByPopup | ImGuiHoveredFlags.AllowWhenDisabled),
                ImGui.IsWindowHovered(), ImGui.IsItemActive(), pressed, released, clicked,
                ImGui.GetIO().WantCaptureMouse, ImGuiP.GetCurrentWindow().Flags, nativeInputOcclusions.Count);
            if (pressed || released)
                lastTabClick = lastTabInput;
        }
        DrawAtNativeDepth(
            itemMin,
            itemMax,
            icon,
            selected,
            hovered,
            held,
            showMissingBadge);

        if (hovered)
            ImGui.SetTooltip(tooltip);
        if (clicked && !mouseOccluded)
            shopper.OpenPage(page, shopperAnchor);
        ImGui.PopID();
    }

    private void DrawAtNativeDepth(
        Vector2 itemMin,
        Vector2 itemMax,
        FontAwesomeIcon icon,
        bool selected,
        bool hovered,
        bool held,
        bool showMissingBadge)
    {
        if (nativeOcclusions.Count == 0)
        {
            DrawContents();
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        MarketBoardTabPresentation.FillVisibleRegions(
            itemMin,
            itemMax - itemMin,
            nativeOcclusions,
            visibleTabRegions,
            tabRegionScratch);
        foreach (var region in visibleTabRegions)
        {
            if (!MarketBoardTabPresentation.Overlaps(
                    itemMin,
                    itemMax - itemMin,
                    region.Minimum,
                    region.Maximum - region.Minimum))
            {
                continue;
            }

            drawList.PushClipRect(region.Minimum, region.Maximum, true);
            DrawContents();
            drawList.PopClipRect();
        }

        // A direct local call keeps captured drawing state on the stack instead
        // of creating an Action and closure for every tab on every frame.
        void DrawContents()
        {
            DrawTabSurface(itemMin, itemMax, icon, selected, hovered, held);
            if (showMissingBadge)
                DrawMissingBadge(itemMin);
        }
    }

    private unsafe bool TryCaptureNativeOcclusions(AtkUnitBase* anchor)
    {
        var manager = RaptureAtkUnitManager.Instance();
        if (manager == null || !TryBuildNativeOrder(manager, anchor))
            return false;

        ref var unitList = ref manager->AtkUnitManager.AllLoadedUnitsList;
        if (unitList.Count > unitList.Entries.Length)
            return false;

        for (var index = 0; index < unitList.Count; index++)
        {
            var candidate = unitList.Entries[index].Value;
            if (candidate == null)
                return false;

            if (!MarketBoardTabPresentation.IsOccludingPanel(candidate == anchor,
                    candidate->IsVisible, candidate->WindowNode != null,
                    NonWindowOccluderNames.Contains(candidate->NameString)))
                continue;

            if (MarketBoardTabPresentation.GetOpacity(
                    candidate->Alpha,
                    candidate->VisibilityState.HasFlag(AtkUnitBaseVisibilityState.Hide),
                    candidate->AppliedVisibilityState.HasFlag(AtkUnitBaseVisibilityState.Hide)) <= 0f)
                continue;

            // Focus is an input target, not proof of draw order. Every visible
            // window must have a current stack entry; never fall back to cached
            // focus or DrawOrderIndex, which is updated after list mutations.
            if (candidate->RootNode == null
                || !nativeOrder.TryGetValue((nint)candidate, out var order))
                return false;

            GetNativeOcclusionGeometry(candidate, out var position, out var size);
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y)
                || !float.IsFinite(size.X) || !float.IsFinite(size.Y)
                || size.X <= 0f || size.Y <= 0f)
                return false;

            nativeWindows.Add(new(
                (nint)candidate,
                order.DepthLayer,
                order.LayerIndex,
                new(position, position + size)));
            GetNativeWindowGeometry(candidate, out var inputPosition, out var inputSize);
            if (!float.IsFinite(inputPosition.X) || !float.IsFinite(inputPosition.Y)
                || !float.IsFinite(inputSize.X) || !float.IsFinite(inputSize.Y)
                || inputSize.X <= 0f || inputSize.Y <= 0f)
                return false;
            nativeInputWindows.Add(new((nint)candidate, order.DepthLayer, order.LayerIndex,
                new(inputPosition, inputPosition + inputSize)));
        }

        return MarketBoardTabPresentation.TryFillNativeOcclusions(
            (nint)anchor,
            nativeWindows,
            MaximumNativeOccluders,
            nativeOcclusions)
            && MarketBoardTabPresentation.TryFillNativeOcclusions(
                (nint)anchor, nativeInputWindows, MaximumNativeOccluders, nativeInputOcclusions);
    }

    private unsafe bool TryBuildNativeOrder(
        RaptureAtkUnitManager* manager,
        AtkUnitBase* anchor)
    {
        var layers = manager->AtkUnitManager.DepthLayers;
        if (anchor->DepthLayer >= layers.Length)
            return false;

        // AtkUnitManager stores each depth layer in native draw order. Validate
        // the complete bounded lists before trusting either side of an overlap.
        for (var layerIndex = 0; layerIndex < layers.Length; layerIndex++)
        {
            ref var layer = ref layers[layerIndex];
            if (layer.Count > layer.Entries.Length)
                return false;

            for (var index = 0; index < layer.Count; index++)
            {
                var candidate = layer.Entries[index].Value;
                if (candidate == null
                    || candidate->DepthLayer != (uint)layerIndex
                    || !nativeOrder.TryAdd((nint)candidate, ((uint)layerIndex, index)))
                    return false;
            }
        }

        return nativeOrder.ContainsKey((nint)anchor);
    }
    private static unsafe void GetNativeOcclusionGeometry(
        AtkUnitBase* addon,
        out Vector2 position,
        out Vector2 size)
    {
        // Mouse-hit rectangles include invisible drag/resize padding. Clip to
        // the window component instead; non-window tooltips use their own bounds.
        if (addon->WindowNode != null)
        {
            FFXIVClientStructs.FFXIV.Common.Math.Bounds bounds;
            addon->WindowNode->GetBounds(&bounds);
            position = new(bounds.Pos1.X, bounds.Pos1.Y);
            size = new(bounds.Width, bounds.Height);
            if (size.X > 0f && size.Y > 0f)
            {
                if (addon->NameString == "InventoryLarge")
                {
                    var frame = MarketBoardTabPresentation.GetInventoryVisibleFrame(position, size);
                    position = frame.Position;
                    size = frame.Size;
                }
                return;
            }
        }
        GetNativeWindowGeometry(addon, out position, out size);
    }

    private static unsafe void GetNativeWindowGeometry(
        AtkUnitBase* addon,
        out Vector2 position,
        out Vector2 size)
    {
        FFXIVClientStructs.FFXIV.Common.Math.Bounds bounds;
        addon->GetWindowBounds(&bounds);
        position = new Vector2(bounds.Pos1.X, bounds.Pos1.Y);
        size = new Vector2(bounds.Width, bounds.Height);
        if (size.X > 0f && size.Y > 0f)
            return;

        position = new Vector2(addon->X, addon->Y);
        size = new Vector2(addon->GetScaledWidth(true), addon->GetScaledHeight(true));
    }

    private void DrawTabSurface(
        Vector2 itemMin,
        Vector2 itemMax,
        FontAwesomeIcon icon,
        bool selected,
        bool hovered,
        bool held)
    {
        var draw = ImGui.GetWindowDrawList();
        var fill = held || selected ? TabSelected : hovered ? TabHovered : Tab;
        var min = itemMin + new Vector2(tabScale);
        // The body occupies 48 logical pixels; its additional two-pixel right
        // extension reaches the native frame. Insets keep the outline inside
        // the existing native clip instead of overriding that clip.
        var max = itemMax - new Vector2(0f, tabScale);
        DrawShopTab(draw, min, max, ImGui.GetColorU32(fill),
            ImGui.GetColorU32(GoldBorder), tabScale, shopper.TabStyle);

        using (var lockedIconFont = pluginInterface.UiBuilder.IconFontHandle.Lock())
        using (pluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var iconText = icon.ToIconString();
            var measured = ImGui.CalcTextSize(iconText);
            // Fit both dimensions; wide category glyphs must retain the same padding.
            var iconBounds = 26f * tabScale;
            var fit = iconBounds / Math.Max(measured.X, measured.Y);
            var iconFontSize = ImGui.GetFontSize() * fit;
            var iconSize = measured * fit;
            var center = itemMin + new Vector2(MarketBoardTabPresentation.TabBodySize * 0.5f * tabScale);
            draw.AddText(lockedIconFont.ImFont, iconFontSize,
                center - (iconSize * 0.5f), ImGui.GetColorU32(Icon), iconText);
        }
    }

    private void DrawShopTab(
        ImDrawListPtr draw,
        Vector2 min,
        Vector2 max,
        uint fill,
        uint border,
        float scale,
        MarketBoardTabPresentation.TabStyle style)
    {
        var radius = 5f * scale;
        var center = (min.Y + max.Y) * 0.5f;
        switch (style)
        {
            case MarketBoardTabPresentation.TabStyle.InsetSockets:
                draw.AddRectFilled(min, max, ImGui.GetColorU32(palette.Inset), radius);
                draw.AddRect(min, max, border, radius);
                var inset = new Vector2(3f * scale);
                draw.AddRectFilled(min + inset, max - inset, fill, 3f * scale);
                draw.AddRect(min + inset, max - inset, border, 3f * scale);
                break;
            case MarketBoardTabPresentation.TabStyle.RoundTabs:
                var round = (max.Y - min.Y) * 0.5f;
                draw.AddCircleFilled(new Vector2(min.X + round, center), round, fill, 24);
                draw.AddRectFilled(new Vector2(min.X + round, min.Y), max, fill);
                // One continuous bottom-left-top outline, open at the frame.
                // Keep every command under DrawAtNativeDepth's current scissor.
                draw.PathClear();
                draw.PathLineTo(new Vector2(max.X, max.Y));
                draw.PathArcTo(new Vector2(min.X + round, center), round,
                    MathF.PI * 0.5f, MathF.PI * 1.5f, 16);
                draw.PathLineTo(new Vector2(max.X, min.Y));
                draw.PathStroke(border, ImDrawFlags.None, 1.5f * scale);
                break;
            case MarketBoardTabPresentation.TabStyle.RibbonTabs:
                var cut = min.X + 5f * scale;
                draw.AddRectFilled(new Vector2(cut, min.Y), max, fill);
                draw.AddTriangleFilled(min, new Vector2(cut, min.Y), new Vector2(cut, center), fill);
                draw.AddTriangleFilled(new Vector2(min.X, max.Y),
                    new Vector2(cut, center), new Vector2(cut, max.Y), fill);
                draw.AddLine(min, new Vector2(max.X, min.Y), border, scale);
                draw.AddLine(new Vector2(min.X, max.Y), max, border, scale);
                draw.AddLine(min, new Vector2(cut, center), border, scale);
                draw.AddLine(new Vector2(cut, center), new Vector2(min.X, max.Y), border, scale);
                break;
            case MarketBoardTabPresentation.TabStyle.BeveledIndex:
                var bevel = 5f * scale;
                draw.AddRectFilled(new Vector2(min.X + bevel, min.Y), max, fill);
                draw.AddRectFilled(new Vector2(min.X, min.Y + bevel),
                    new Vector2(min.X + bevel, max.Y - bevel), fill);
                draw.AddTriangleFilled(new Vector2(min.X + bevel, min.Y),
                    new Vector2(min.X + bevel, min.Y + bevel), new Vector2(min.X, min.Y + bevel), fill);
                draw.AddTriangleFilled(new Vector2(min.X, max.Y - bevel),
                    new Vector2(min.X + bevel, max.Y - bevel), new Vector2(min.X + bevel, max.Y), fill);
                draw.AddLine(new Vector2(min.X + bevel, min.Y), new Vector2(max.X, min.Y), border, scale);
                draw.AddLine(new Vector2(min.X + bevel, max.Y), max, border, scale);
                draw.AddLine(new Vector2(min.X + bevel, min.Y), new Vector2(min.X, min.Y + bevel), border, scale);
                draw.AddLine(new Vector2(min.X, min.Y + bevel), new Vector2(min.X, max.Y - bevel), border, scale);
                draw.AddLine(new Vector2(min.X, max.Y - bevel), new Vector2(min.X + bevel, max.Y), border, scale);
                break;
            default:
                draw.AddRectFilled(min, max, fill, 3f * scale, ImDrawFlags.RoundCornersLeft);
                draw.AddRect(min, max, border, 3f * scale, ImDrawFlags.RoundCornersLeft);
                draw.AddLine(new Vector2(max.X, min.Y + scale),
                    new Vector2(max.X, max.Y - scale), fill, 2f * scale);
                break;
        }
    }

    private void DrawMissingBadge(Vector2 itemMin)
    {
        var center = itemMin + new Vector2(38f, 37f) * tabScale;
        var draw = ImGui.GetWindowDrawList();
        draw.AddCircleFilled(center, 7f * tabScale, ImGui.GetColorU32(Badge));
        draw.AddCircle(center, 7f * tabScale, ImGui.GetColorU32(GoldBorder), 20, tabScale);
        var fontSize = 12f * tabScale;
        var textSize = ImGui.CalcTextSize("?") * (fontSize / ImGui.GetFontSize());
        draw.AddText(ImGui.GetFont(), fontSize, center - textSize * 0.5f,
            ImGui.GetColorU32(Icon), "?");
    }
}



