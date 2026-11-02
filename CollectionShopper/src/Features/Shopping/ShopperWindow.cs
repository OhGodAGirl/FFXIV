using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace CollectionShopper;

internal class ShopperWindow : Window, IDisposable
{
    private const float ResponsiveBreakpoint = 900f;
    private const int MaximumCachedItemIcons = 8_192;
    private const int MaximumCachedCollectionIcons = 4_096;

    private readonly MarketBoardSettingsView marketBoardSettings;
    private readonly ShopperCategorySettingsView categorySettings;
    private readonly Plugin plugin;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly FfxivCollectClient client;
    private readonly ShoppingController shoppingController;
    private readonly IFramework framework;
    private readonly IPlayerState playerState;
    private readonly IDataManager dataManager;
    private readonly ITextureProvider textureProvider;
    private readonly IPluginLog log;
    private readonly IFontHandle sectionFont;
    private readonly IFontHandle nativeGroupFont;
    private readonly SingleFlightOperation refreshOperation;
    private readonly RefreshActivity refreshActivity = new();
    private readonly CollectionRefreshRequests refreshRequests = new();
    private readonly CollectionDataOwnershipSession collectionDataSession = new();
    private readonly byte[] filterBuffer = new byte[128];
    private readonly Dictionary<uint, ISharedImmediateTexture> itemIcons = [];
    private readonly Dictionary<string, ISharedImmediateTexture> collectionIcons = [];
    private readonly HashSet<string> failedCollectionIcons = [];
    private ShoppingCandidate[] candidates = [];
    private ShoppingCandidate[] catalogCandidates = [];
    private IReadOnlyList<Outfit> catalogOutfits = [];
    private IReadOnlyDictionary<uint, MarketPriceQuote> prices = new Dictionary<uint, MarketPriceQuote>();
    private ShoppingItem[] items = [];
    private string status = "Press Refresh to load missing purchasable collection items.";
    private string? priceMarket;
    private string? remoteWarning;
    private bool collectionLoaded;
    private bool stylesPushed;
    private IReadOnlyList<MarketBoardTabPresentation.ClipRegion> nativeOcclusions = [];
    private readonly NativeWindowDrawClipper nativeDrawClipper = new();
    private bool nativeOcclusionAvailable = true;
    private IReadOnlyList<MarketBoardTabPresentation.ClipRegion> nativeInputOcclusions = [];
    private Vector2? nextPosition;
    private Vector2? nextSize;
    private bool buyAllRunActive;
    private uint lastCenteredItemId;
    private readonly VisibleShoppingItemsCache visibleItemsCache = new();
    private int itemsRevision;
    private int completionRevision;
    private bool disposed;

    public ShopperPage ActivePage { get; private set; } = ShopperPage.Settings;

    internal ShopperWindow(
        Plugin plugin,
        IDalamudPluginInterface pluginInterface,
        FfxivCollectClient client,
        ShoppingController shoppingController,
        IFramework framework,
        IPlayerState playerState,
        IDataManager dataManager,
        ITextureProvider textureProvider,
        IPluginLog log,
        CancellationToken cancellationToken)
        : base(PluginIdentity.WindowId)
    {
        this.plugin = plugin;
        this.pluginInterface = pluginInterface;
        this.client = client;
        this.shoppingController = shoppingController;
        this.framework = framework;
        this.playerState = playerState;
        this.dataManager = dataManager;
        this.textureProvider = textureProvider;
        this.log = log;
        refreshOperation = new SingleFlightOperation(cancellationToken);
        using (pluginInterface.UiBuilder.FontAtlas.SuppressAutoRebuild())
        {
            sectionFont = pluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(
                new GameFontStyle(GameFontFamily.MiedingerMid, 17f));
            nativeGroupFont = pluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(
                new GameFontStyle(GameFontFamily.Jupiter, 22f));
        }
        Size = new Vector2(1_200, 700);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = Vector2.One,
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        marketBoardSettings = new MarketBoardSettingsView(plugin, shoppingController);
        categorySettings = new ShopperCategorySettingsView(plugin, GetCategoryMissingDisplay, EnsureActivePageAvailable);
    }

    internal bool IsCollectionLoaded => collectionLoaded;
    internal bool IsCatalogReady => catalogOutfits.Count > 0;

    internal bool IsRefreshingCollection => refreshActivity.IsActive;

    internal CollectionDataOwnershipState CollectionDataOwnership => collectionDataSession.State;

    public bool HasMissingOutfits => HasMissing(ShoppingCategory.Outfit);

    public bool HasMissingWeapons => HasMissing(ShoppingCategory.Weapon);

    public bool HasMissingMinions => HasMissing(ShoppingCategory.Minion);

    public bool HasMissingMounts => HasMissing(ShoppingCategory.Mount);

    public bool HasMissingOrchestrions => HasMissing(ShoppingCategory.Orchestrion);

    public bool HasMissingHairstyles => HasMissing(ShoppingCategory.Hairstyle);

    public bool HasMissingFacewear => HasMissing(ShoppingCategory.Facewear);

    public bool HasMissingFashion => HasMissing(ShoppingCategory.Fashion);

    public bool HasAnyMissing => items.Any(item => IsMissing(item));

    public bool ShouldShowOutfitsBookmark => categorySettings.IsCategoryTabVisible(ShoppingCategory.Outfit);

    public bool ShouldShowWeaponsBookmark => categorySettings.IsCategoryTabVisible(ShoppingCategory.Weapon);

    public bool ShouldShowMinionsBookmark => categorySettings.IsCategoryTabVisible(ShoppingCategory.Minion);

    public bool ShouldShowMountsBookmark => categorySettings.IsCategoryTabVisible(ShoppingCategory.Mount);

    public bool ShouldShowOrchestrionsBookmark => categorySettings.IsCategoryTabVisible(ShoppingCategory.Orchestrion);

    public bool ShouldShowHairstylesBookmark => categorySettings.IsCategoryTabVisible(ShoppingCategory.Hairstyle);

    public bool ShouldShowFacewearBookmark => categorySettings.IsCategoryTabVisible(ShoppingCategory.Facewear);

    public bool ShouldShowFashionBookmark => categorySettings.IsCategoryTabVisible(ShoppingCategory.Fashion);

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        refreshRequests.Dispose();
        refreshOperation.Dispose();
        collectionDataSession.Dispose();
        itemIcons.Clear();
        collectionIcons.Clear();
        failedCollectionIcons.Clear();
        nativeGroupFont.Dispose();
        sectionFont.Dispose();
    }

    public override bool DrawConditions() => nativeOcclusionAvailable;

    public override void PreDraw()
    {
        nativeDrawClipper.BeginFrame();
        AllowBackgroundBlur = nativeOcclusions.Count == 0;
        Flags = MarketBoardTabPresentation.IsPointOccluded(ImGui.GetMousePos(), nativeInputOcclusions)
            ? ImGuiWindowFlags.NoInputs
            : ImGuiWindowFlags.None;
        NativeWindowDrawClipper.SetTreeMouseInputBlocked(
            ImGuiP.FindWindowByName(WindowName), Flags.HasFlag(ImGuiWindowFlags.NoMouseInputs));
        // Preserve host-calculated hover and capture for unobstructed input.
        // Recalculate only to yield to a native foreground window.
        if (Flags.HasFlag(ImGuiWindowFlags.NoMouseInputs))
            ImGuiP.UpdateHoveredWindowAndCaptureFlags();
        if (nextPosition is { } position)
        {
            ImGui.SetNextWindowPos(position, ImGuiCond.Always);
            nextPosition = null;
        }
        if (nextSize is { } size)
        {
            // Native bounds are already physical pixels. Do not let WindowHost
            // replace them with the scaled first-use size after PreDraw.
            Size = null;
            ImGui.SetNextWindowSize(size, ImGuiCond.Always);
            nextSize = null;
        }

        PluginTheme.Push();
        stylesPushed = true;
    }

    public override void PostDraw()
    {
        try
        {
            // Find the parent even when collapsed: its title/background still draw.
            var parent = ImGuiP.FindWindowByName(WindowName);
            if (!parent.IsNull)
                nativeDrawClipper.Capture(parent.DrawList);
            nativeDrawClipper.FinishFrame(nativeOcclusions);
        }
        finally
        {
            if (stylesPushed)
            {
                PluginTheme.Pop();
                stylesPushed = false;
            }
        }
    }

    public override void Draw()
    {
        if (ActivePage == ShopperPage.Settings)
            DrawSettingsPage();
        else
            DrawShoppingPage(ActivePage);
    }

    public void OpenPage(ShopperPage page, Vector2? position = null)
    {
        ActivePage = page;
        nextPosition = position;
        IsOpen = true;
    }

    public void MatchMarketBoard(
        Vector2 position,
        Vector2 size,
        IReadOnlyList<MarketBoardTabPresentation.ClipRegion> foregroundOcclusions,
        IReadOnlyList<MarketBoardTabPresentation.ClipRegion> foregroundInputOcclusions)
    {
        ArgumentNullException.ThrowIfNull(foregroundOcclusions);
        nextPosition = position;
        nextSize = size;
        nativeOcclusions = foregroundOcclusions;
        nativeInputOcclusions = foregroundInputOcclusions;
        nativeOcclusionAvailable = true;
    }

    internal void SetNativeOcclusionUnavailable() => nativeOcclusionAvailable = false;

    internal void ClearMarketBoardAttachment()
    {
        nativeOcclusions = [];
        nativeInputOcclusions = [];
        nativeOcclusionAvailable = true;
    }

    public void EnsureCollectionLoaded()
    {
        if (collectionLoaded)
            return;

        StartRefresh();
    }

    internal void RequestMarketPriceRefresh()
        => refreshRequests.RequestPrices();

    internal void TryStartRequestedMarketPriceRefresh()
    {
        if (disposed || IsRefreshBusy)
            return;

        if (refreshRequests.TryTakeCatalog(collectionLoaded, IsRefreshBusy))
        {
            BeginCollectionRefresh();
            return;
        }

        if (refreshRequests.TryTakePrices(IsRefreshBusy || plugin.ReadCollectionDataOwnership().Snapshot is null))
            StartMarketPriceRefresh();
    }

    internal void NotifyPurchaseTrackingChanged()
    {
        completionRevision++;
        var contentId = plugin.CurrentContentId;
        PublishCollectionDataOwnership(contentId, collectionDataSession.Begin(contentId), CancellationToken.None);
    }

    public void NotifyPurchased(uint itemId)
    {
        completionRevision++;
        var item = items.FirstOrDefault(candidate => candidate.ItemId == itemId);
        if (item is not null)
            status = $"Purchased {item.ItemName}.";

        EnsureActivePageAvailable();
    }

    internal void TryRefreshCollectionDataOwnership()
    {
        // Prepared ownership can change while catalog or price I/O is still pending.
        if (disposed || !plugin.TryTakeCollectionDataRefreshRequest())
        {
            return;
        }

        var currentContentId = plugin.CurrentContentId;
        var sessionVersion = collectionDataSession.Begin(currentContentId);
        PublishCollectionDataOwnership(currentContentId, sessionVersion, CancellationToken.None);

        if (CollectionOwnershipRefreshPolicy.RequiresCatalogRefresh(
                collectionLoaded,
                plugin.Configuration.AutomaticallyRefreshCollection))
        {
            StartRefresh();
        }
    }

    internal void InvalidateCharacterContext(string message)
    {
        collectionDataSession.Invalidate(plugin.CurrentContentId);
        collectionLoaded = false;
        refreshRequests.Invalidate(plugin.CurrentContentId != 0 && plugin.Configuration.AutomaticallyRefreshCollection);
        priceMarket = null;
        remoteWarning = null;
        items = [];
        candidates = [];
        catalogCandidates = [];
        catalogOutfits = [];
        prices = new Dictionary<uint, MarketPriceQuote>();
        itemsRevision++;
        EnsureActivePageAvailable();
        status = message;
    }

    private static Vector4 Gold => PluginTheme.Gold;
    private static Vector4 Text => PluginTheme.Text;
    private static Vector4 TextMuted => PluginTheme.TextMuted;

    private void DrawShoppingPage(ShopperPage page)
    {
        var title = ShoppingListText.GetPageTitle(page);
        var visibleItems = GetVisibleItems(page);

        if (ImGui.GetContentRegionAvail().X >= 780f
            && ImGui.BeginTable("##shopping-header", 2,
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings))
        {
            ImGui.TableSetupColumn("##title", ImGuiTableColumnFlags.WidthStretch, 1.3f);
            ImGui.TableSetupColumn("##filter-column", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableNextColumn();
            DrawSectionHeading(title);
            DrawInlineShoppingStatus();
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##filter", "Filter Item or Collection", filterBuffer.AsSpan());
            ImGui.EndTable();
        }
        else
        {
            DrawSectionHeading(title);
            DrawInlineShoppingStatus();
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##filter", "Filter Item or Collection", filterBuffer.AsSpan());
        }

        ImGui.Separator();

        if (visibleItems.Length == 0)
        {
            var emptyState = ShoppingProjection.ResolveEmptyState(
                collectionLoaded,
                IsPageOwnershipReady(page),
                !string.IsNullOrWhiteSpace(ShoppingListText.ReadBuffer(filterBuffer)));
            ImGui.TextWrapped(emptyState switch
            {
                ShoppingEmptyState.NotLoaded =>
                    "Open Settings and refresh collection data to load Market Board candidates.",
                ShoppingEmptyState.Filtered =>
                    $"No {title.ToLowerInvariant()} match the current filters.",
                ShoppingEmptyState.OwnershipUnavailable =>
                    collectionDataSession.State.StatusDetail,
                _ => "You've collected everything currently available on the Market Board!",
            });
            return;
        }

        DrawCollectionPanel(title, visibleItems, false, page);
    }

    private void DrawInlineShoppingStatus()
    {
        if (!ShouldShowShoppingStatus())
            return;

        ImGui.SameLine();
        var start = ImGui.GetCursorScreenPos();
        var width = Math.Max(1f, ImGui.GetContentRegionAvail().X);
        var size = new Vector2(width, ImGui.GetTextLineHeight());
        var message = shoppingController.IsRunning ? shoppingController.Status : status;
        // Reserve only one line; long messages cannot resize the header or list.
        ImGui.Dummy(size);
        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(start, start + size, true);
        drawList.AddText(start, ImGui.GetColorU32(TextMuted), message);
        drawList.PopClipRect();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(message);
    }

    internal MarketBoardTabPresentation.TabStyle TabStyle
        => MarketBoardTabPresentation.NormalizeTabStyle(plugin.Configuration.MarketBoardTabStyle);

    private void DrawTabStyleSettings()
    {
        ImGui.SetNextItemWidth(Math.Min(220f * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo("Tab Style", MarketBoardTabPresentation.GetTabStyleName(TabStyle)))
        {
            for (var index = 0; index < 5; index++)
            {
                var style = (MarketBoardTabPresentation.TabStyle)index;
                if (ImGui.Selectable(MarketBoardTabPresentation.GetTabStyleName(style), style == TabStyle))
                {
                    plugin.Configuration.MarketBoardTabStyle = style;
                    plugin.SaveConfiguration();
                }
            }
            ImGui.EndCombo();
        }
        ImGui.Spacing();
    }

    private void DrawSettingsPage()
    {
        DrawTabStyleSettings();
        if (ImGui.GetContentRegionAvail().X < ResponsiveBreakpoint)
        {
            DrawSettingsPanel("Collection", DrawCollectionSettings);
            ImGui.Spacing();
            DrawSettingsPanel("Market Board", marketBoardSettings.Draw);
            ImGui.Spacing();
            DrawSettingsPanel("Automated Purchasing", DrawAutomaticPurchaseList);
        }
        else if (ImGui.BeginTable(
            "##settings-columns",
            2,
            ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.PadOuterX))
        {
            ImGui.TableNextColumn();
            DrawSettingsPanel("Collection", DrawCollectionSettings);
            ImGui.Spacing();
            DrawSettingsPanel("Market Board", marketBoardSettings.Draw);

            ImGui.TableNextColumn();
            DrawSettingsPanel("Automated Purchasing", DrawAutomaticPurchaseList);
            ImGui.EndTable();
        }

        DrawSettingsStatusFooter();
    }

    private void DrawSettingsStatusFooter()
    {
        const float footerHeight = 78f;
        var remainingHeight = ImGui.GetContentRegionAvail().Y;
        if (remainingHeight > footerHeight + ImGui.GetStyle().ItemSpacing.Y)
        {
            ImGui.Dummy(new Vector2(
                0f,
                remainingHeight - footerHeight - ImGui.GetStyle().ItemSpacing.Y));
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawNativeGroupHeading("Collection Status");
        ImGui.Indent(4f);
        ImGui.PushStyleColor(ImGuiCol.Text, TextMuted);
        ImGui.TextWrapped(status);
        ImGui.PopStyleColor();
        ImGui.Unindent(4f);
    }

    private void DrawSettingsPanel(string title, System.Action drawContents)
    {
        ImGui.PushID(title);
        DrawNativeGroupHeading(title);
        ImGui.Indent(4f);
        drawContents();
        ImGui.Unindent(4f);
        ImGui.PopID();
    }

    private void DrawSectionHeading(string title)
    {
        using (sectionFont.Push())
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Gold);
            ImGui.TextUnformatted(title);
            ImGui.PopStyleColor();
        }
    }

    private void DrawNativeGroupHeading(string title)
    {
        using (nativeGroupFont.Push())
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.84f, 0.71f, 0.47f, 1f));
            ImGui.TextUnformatted(title);
            ImGui.PopStyleColor();
        }
    }

    private void DrawCollectionSettings()
    {
        ImGui.BeginDisabled(refreshActivity.IsActive);
        try
        {
            if (ImGui.Button(refreshActivity.IsActive ? "Refreshing..." : "Refresh"))
                StartRefresh();
        }
        finally
        {
            ImGui.EndDisabled();
        }

        var automaticallyRefresh = plugin.Configuration.AutomaticallyRefreshCollection;
        if (ImGui.Checkbox("Automatically Refresh Collection", ref automaticallyRefresh))
        {
            plugin.Configuration.AutomaticallyRefreshCollection = automaticallyRefresh;
            plugin.SaveConfiguration();
            if (automaticallyRefresh)
                EnsureCollectionLoaded();
        }
        PluginControls.DrawHelpMarker("Auto-refresh catalogs and prices.");

        var hideOwned = plugin.Configuration.HideLocallyOwned;
        if (ImGui.Checkbox("Hide Locally Owned", ref hideOwned))
        {
            plugin.Configuration.HideLocallyOwned = hideOwned;
            plugin.SaveConfiguration();
        }

        PluginControls.DrawWrappedNote(CollectionDataOwnershipPolicy.BuildPlayerSummary(collectionDataSession.State));
        if (ImGui.IsItemHovered())
            PluginControls.DrawTooltip(collectionDataSession.State.StatusDetail);
    }

    private void DrawCollectionPanel(
        string title,
        ShoppingItem[] panelItems,
        bool showHeading = true,
        ShopperPage? page = null)
    {
        ImGui.PushID(title);
        if (showHeading)
        {
            using (sectionFont.Push())
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Gold);
                ImGui.TextUnformatted(title);
                ImGui.PopStyleColor();
            }
            ImGui.SameLine();
            ImGui.TextDisabled($"{GetMissingCount(panelItems):N0} Missing");
        }

        if (panelItems.Length == 0)
        {
            ImGui.TextDisabled($"No {title.ToLowerInvariant()} match the current filters.");
            ImGui.PopID();
            return;
        }

        var flags = ImGuiTableFlags.BordersInnerH
            | ImGuiTableFlags.RowBg
            | ImGuiTableFlags.ScrollY
            | ImGuiTableFlags.NoSavedSettings
            | ImGuiTableFlags.SizingStretchProp;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(10f, 6f));
        if (!ImGui.BeginTable("##items-render-v3", 4, flags, new Vector2(0, -50f)))
        {
            ImGui.PopStyleVar();
            ImGui.PopID();
            return;
        }
        NativeWindowDrawClipper.SetMouseInputBlocked(
            ImGuiP.GetCurrentWindow(), Flags.HasFlag(ImGuiWindowFlags.NoMouseInputs));
        nativeDrawClipper.Capture(ImGui.GetWindowDrawList());
        ImGui.TableSetupScrollFreeze(0, 1);
        var collectionHeader = ShoppingListText.GetCollectionColumnTitle(panelItems);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 1.8f);
        ImGui.TableSetupColumn(collectionHeader, ImGuiTableColumnFlags.WidthStretch, 1.55f);
        ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 180f);
        ImGui.TableSetupColumn("Buy", ImGuiTableColumnFlags.WidthFixed, 112f);
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        DrawTableHeader(0, "Item");
        DrawTableHeader(1, collectionHeader);
        DrawTableHeader(2, "Price", true);
        DrawTableHeader(3, "Buy", true);

        if (!shoppingController.IsRunning)
            lastCenteredItemId = 0;
        foreach (var item in panelItems)
            DrawItemRow(item);

        ImGui.EndTable();
        ImGui.PopStyleVar();
        DrawListFooter(panelItems, page);
        ImGui.PopID();
    }

    private int GetMissingCount(IEnumerable<ShoppingItem> panelItems)
        => panelItems.Count(IsMissing);

    private void DrawItemRow(ShoppingItem item)
    {
        ImGui.PushID((int)item.ItemId);
        var isActivePurchase = shoppingController.IsRunning
            && shoppingController.CurrentItemId == item.ItemId;
        ImGui.TableNextRow(ImGuiTableRowFlags.None, 68f);
        if (isActivePurchase)
        {
            var pulse = PluginMotion.WorkingPulse(
                (float)ImGui.GetTime(),
                plugin.Configuration.ReduceMotion);
            ImGui.TableSetBgColor(
                ImGuiTableBgTarget.RowBg0,
                ImGui.GetColorU32(new Vector4(0.44f, 0.36f, 0.18f, 0.28f + (pulse * 0.12f))));
        }
        ImGui.TableSetColumnIndex(0);
        DrawItemIcon(item);
        var iconMin = ImGui.GetItemRectMin();
        var iconMax = ImGui.GetItemRectMax();
        if (isActivePurchase)
            DrawActivePurchaseMarker(iconMin, iconMax, plugin.Configuration.ReduceMotion);
        ImGui.SameLine(0f, 8f);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(item.ItemName);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(string.IsNullOrWhiteSpace(item.Patch)
                ? item.Source
                : $"Patch {item.Patch}\n{item.Source}");

        ImGui.TableSetColumnIndex(1);
        var collectionIconHovered = false;
        if (!string.IsNullOrWhiteSpace(item.CollectionIconPath))
        {
            DrawCollectionIcon(item);
            collectionIconHovered = ImGui.IsItemHovered();
            ImGui.SameLine(0f, 8f);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY()
                + Math.Max(0f, (48f - ImGui.GetTextLineHeight()) * 0.5f));
        }
        else
        {
            ImGui.AlignTextToFramePadding();
        }
        ImGui.TextUnformatted(item.CollectionName);
        if ((collectionIconHovered || ImGui.IsItemHovered()) && item.Category == ShoppingCategory.Outfit)
        {
            ImGui.SetTooltip(
                $"{item.RemainingPiecesInCollection:N0} Piece(s) Remaining When the List Was Refreshed.");
        }

        ImGui.TableSetColumnIndex(2);
        var price = GetPreferredPrice(item);
        if (price is null)
        {
            DrawRightAlignedRowText(ShoppingListPolicy.MissingPriceLabel(refreshActivity.IsActive), 158f, 56f);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(ShoppingListPolicy.MissingPriceTooltip);
        }
        else
            DrawGilAmount(price.PricePerUnit, price.IsHighQuality, 158f, 56f);

        ImGui.TableSetColumnIndex(3);
        var unavailable = !ShoppingListPolicy.CanBuy(item, plugin.PurchaseSuppressionIds, shoppingController.IsRunning);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 10f);
        if (PluginControls.DrawNativeButton("Buy", new Vector2(94f, 36f), unavailable))
        {
            shoppingController.StartSingle(item);
            status = shoppingController.Status;
        }
        if (!item.OwnershipKnown && ImGui.IsItemHovered())
            ImGui.SetTooltip(collectionDataSession.State.Classify(item.Category, item.ItemId, item.CollectionId, item.MappingValid).Reason);
        if (isActivePurchase && lastCenteredItemId != item.ItemId)
        {
            if (!plugin.Configuration.ReduceMotion || !ImGui.IsRectVisible(iconMin, iconMax))
                ImGui.SetScrollHereY(0.5f);
            lastCenteredItemId = item.ItemId;
        }
        ImGui.PopID();
    }

    private static void DrawActivePurchaseMarker(
        Vector2 iconMin,
        Vector2 iconMax,
        bool reduceMotion)
    {
        var drawList = ImGui.GetWindowDrawList();
        var pulse = PluginMotion.WorkingPulse((float)ImGui.GetTime(), reduceMotion);
        var gold = new Vector4(0.95f, 0.76f, 0.34f, 0.72f + (pulse * 0.28f));
        drawList.AddRect(
            iconMin - new Vector2(2f, 2f),
            iconMax + new Vector2(2f, 2f),
            ImGui.GetColorU32(gold),
            4f,
            ImDrawFlags.RoundCornersAll,
            2.25f);
        drawList.AddRectFilled(
            new Vector2(iconMin.X - 9f, iconMin.Y - 4f),
            new Vector2(iconMin.X - 5f, iconMax.Y + 4f),
            ImGui.GetColorU32(gold),
            2f);

        var badgeCenter = iconMax - new Vector2(4f, 4f);
        const float badgeRadius = 10f;
        drawList.AddCircleFilled(
            badgeCenter,
            badgeRadius,
            ImGui.GetColorU32(new Vector4(0.11f, 0.105f, 0.09f, 0.98f)),
            20);
        drawList.AddCircle(
            badgeCenter,
            badgeRadius,
            ImGui.GetColorU32(gold),
            20,
            1.75f);
        var handAngle = PluginMotion.WorkingClockAngle(ImGui.GetTime(), reduceMotion);
        var handDirection = new Vector2(MathF.Cos(handAngle), MathF.Sin(handAngle));
        drawList.AddLine(
            badgeCenter + (handDirection * 2.5f),
            badgeCenter + (handDirection * 7f),
            ImGui.GetColorU32(gold),
            2.5f);
        drawList.AddCircleFilled(badgeCenter, 2f, ImGui.GetColorU32(gold), 10);
    }

    private static void DrawTableHeader(int column, string label, bool centered = false)
    {
        ImGui.TableSetColumnIndex(column);
        if (centered)
        {
            var availableWidth = ImGui.GetContentRegionAvail().X;
            var labelWidth = ImGui.CalcTextSize(label).X;
            if (availableWidth > labelWidth)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((availableWidth - labelWidth) * 0.5f));
        }

        ImGui.TextUnformatted(label);
    }

    private static void DrawRightAlignedRowText(string text, float alignWithin, float availableHeight)
    {
        var textSize = ImGui.CalcTextSize(text);
        if (alignWithin > textSize.X)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + alignWithin - textSize.X);
        if (availableHeight > textSize.Y)
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ((availableHeight - textSize.Y) * 0.5f));
        ImGui.TextUnformatted(text);
    }

    private void DrawItemIcon(ShoppingItem item)
    {
        if (item.IconId == 0 || !itemIcons.TryGetValue(item.IconId, out var texture))
        {
            ImGui.Dummy(new Vector2(48f, 48f));
            return;
        }

        var wrap = texture.GetWrapOrEmpty();
        if (wrap.Handle.Handle == 0)
            ImGui.Dummy(new Vector2(48f, 48f));
        else
            ImGui.Image(wrap.Handle, new Vector2(48f, 48f));
    }

    private void DrawCollectionIcon(ShoppingItem item)
    {
        const float iconSize = 48f;
        if (string.IsNullOrWhiteSpace(item.CollectionIconPath)
            || failedCollectionIcons.Contains(item.CollectionIconPath)
            || !collectionIcons.TryGetValue(item.CollectionIconPath, out var texture))
        {
            ImGui.Dummy(new Vector2(iconSize, iconSize));
            return;
        }

        var wrap = texture.GetWrapOrEmpty();
        if (wrap.Handle.Handle == 0)
            ImGui.Dummy(new Vector2(iconSize, iconSize));
        else
            ImGui.Image(wrap.Handle, new Vector2(iconSize, iconSize));
    }

    private void PrepareTextureCaches()
    {
        itemIcons.Clear();
        collectionIcons.Clear();
        failedCollectionIcons.Clear();
        foreach (var item in items)
        {
            if (item.IconId != 0
                && itemIcons.Count < MaximumCachedItemIcons
                && !itemIcons.ContainsKey(item.IconId))
            {
                itemIcons[item.IconId] = textureProvider.GetFromGameIcon(
                    new GameIconLookup(item.IconId));
            }

            var path = item.CollectionIconPath;
            if (string.IsNullOrWhiteSpace(path)
                || collectionIcons.ContainsKey(path)
                || failedCollectionIcons.Contains(path)
                || collectionIcons.Count + failedCollectionIcons.Count >= MaximumCachedCollectionIcons)
            {
                continue;
            }

            try
            {
                collectionIcons[path] = textureProvider.GetFromGame(path);
            }
            catch (Exception ex)
            {
                failedCollectionIcons.Add(path);
                log.Warning(ex, "Could not load collection icon {Path}.", path);
            }
        }
    }

    private void DrawListFooter(ShoppingItem[] panelItems, ShopperPage? page)
    {
        var estimateAvailable = page is { } currentPage ? IsPageOwnershipReady(currentPage) : panelItems.All(item => item.OwnershipKnown);
        var summary = ShoppingListPolicy.Summarize(page is null ? panelItems : items, page,
            categorySettings.IsAutomaticCategoryEnabled, plugin.PurchaseSuppressionIds,
            plugin.Configuration.EnableHighQualityPurchases);
        var purchaseCount = summary.Count;
        var estimate = summary.Total;
        if (buyAllRunActive && !shoppingController.IsRunning)
            buyAllRunActive = false;
        var showProgress = buyAllRunActive && shoppingController.IsRunning;
        if (!ImGui.BeginTable("##shopping-footer", 6,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings))
            return;

        ImGui.TableSetupColumn("##item-count", ImGuiTableColumnFlags.WidthFixed,
            Math.Max(112f, ImGui.CalcTextSize("Items unknown").X + ImGui.GetStyle().CellPadding.X * 2f));
        ImGui.TableSetupColumn("##footer-left-space", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##footer-center", ImGuiTableColumnFlags.WidthFixed, 340f);
        ImGui.TableSetupColumn("##footer-inline-action", ImGuiTableColumnFlags.WidthFixed, 48f);
        ImGui.TableSetupColumn("##footer-right-space", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##footer-main-action", ImGuiTableColumnFlags.WidthFixed, 112f);
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY()
            + Math.Max(0f, (36f - ImGui.GetTextLineHeight()) * 0.5f));
        ImGui.TextDisabled(estimateAvailable ? $"{purchaseCount:N0} items" : "Items unknown");

        ImGui.TableSetColumnIndex(2);
        if (showProgress)
        {
            var finished = shoppingController.InitialCount - shoppingController.RemainingCount;
            var progress = shoppingController.InitialCount == 0
                ? 0f
                : (float)finished / shoppingController.InitialCount;
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 4f);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.13f, 0.135f, 0.13f, 1f));
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.62f, 0.52f, 0.32f, 1f));
            ImGui.ProgressBar(
                progress,
                new Vector2(-1f, 28f),
                $"{finished:N0} / {shoppingController.InitialCount:N0} - {shoppingController.Spent:N0} gil");
            ImGui.PopStyleColor(2);

            ImGui.TableSetColumnIndex(3);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 1f);
            using (pluginInterface.UiBuilder.IconFontHandle.Push())
            {
                if (PluginControls.DrawNativeButton(
                        FontAwesomeIcon.Times.ToIconString(),
                        new Vector2(40f, 34f),
                        false,
                        Gold))
                {
                    shoppingController.Stop("Automated purchasing cancelled.");
                    status = shoppingController.Status;
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Cancel Automated Purchasing.");
        }
        else
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 5f);
            var showEstimate = summary.ShowEstimate(estimateAvailable);
            var estimatedWidth = showEstimate
                ? ImGui.CalcTextSize($"{estimate:N0}").X + 8f + 26f
                : ImGui.CalcTextSize("No estimate").X;
            var centerWidth = ImGui.GetContentRegionAvail().X;
            if (centerWidth > estimatedWidth)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((centerWidth - estimatedWidth) * 0.5f));
            if (showEstimate)
                DrawGilAmount(estimate);
            else
                ImGui.TextDisabled("No estimate");
        }

        if (page is not null && !showProgress)
        {
            ImGui.TableSetColumnIndex(5);
            if (PluginControls.DrawNativeButton("Buy All", new Vector2(104f, 36f),
                shoppingController.IsRunning || refreshActivity.IsActive || purchaseCount == 0))
            {
                BeginBuyAll(GetPagePurchaseItems(page.Value).ToArray());
            }
        }
        ImGui.EndTable();
    }

    private static void DrawGilAmount(
        ulong amount,
        bool highQuality = false,
        float alignWithin = 0f,
        float availableHeight = 0f)
    {
        var amountText = highQuality ? $"{amount:N0} HQ" : $"{amount:N0}";
        const float coinDiameter = 26f;
        const float gap = 8f;
        var textSize = ImGui.CalcTextSize(amountText);
        var contentHeight = Math.Max(textSize.Y, coinDiameter);
        var totalWidth = textSize.X + gap + coinDiameter;
        if (alignWithin > totalWidth)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + alignWithin - totalWidth);

        if (availableHeight > contentHeight)
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ((availableHeight - contentHeight) * 0.5f));

        var start = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(totalWidth, contentHeight));
        var drawList = ImGui.GetWindowDrawList();
        var textPosition = start + new Vector2(0f, (contentHeight - textSize.Y) * 0.5f);
        var center = start + new Vector2(
            textSize.X + gap + (coinDiameter * 0.5f),
            contentHeight * 0.5f);

        drawList.AddText(textPosition, ImGui.GetColorU32(Text), amountText);
        drawList.AddCircleFilled(center + new Vector2(1.5f, 2f), 12.5f,
            ImGui.GetColorU32(new Vector4(0.02f, 0.02f, 0.02f, 0.55f)), 20);
        drawList.AddCircleFilled(center, 12.5f,
            ImGui.GetColorU32(new Vector4(0.88f, 0.67f, 0.23f, 1f)), 20);
        drawList.AddCircle(center, 12.5f,
            ImGui.GetColorU32(new Vector4(0.42f, 0.27f, 0.06f, 1f)), 20, 1.75f);
        drawList.AddCircle(center, 8.5f,
            ImGui.GetColorU32(new Vector4(0.99f, 0.86f, 0.47f, 0.92f)), 20, 1.25f);
        drawList.AddLine(center + new Vector2(-3.5f, -4.5f), center + new Vector2(3.5f, -4.5f),
            ImGui.GetColorU32(new Vector4(0.48f, 0.31f, 0.07f, 0.95f)), 1.5f);
        drawList.AddLine(center + new Vector2(-3.5f, 0f), center + new Vector2(3.5f, 0f),
            ImGui.GetColorU32(new Vector4(0.48f, 0.31f, 0.07f, 0.95f)), 1.5f);
        drawList.AddLine(center + new Vector2(-3.5f, 4.5f), center + new Vector2(3.5f, 4.5f),
            ImGui.GetColorU32(new Vector4(0.48f, 0.31f, 0.07f, 0.95f)), 1.5f);
        drawList.AddCircleFilled(center - new Vector2(4.2f, 4.4f), 1.8f,
            ImGui.GetColorU32(new Vector4(1f, 0.95f, 0.72f, 0.98f)), 8);
    }

    private IEnumerable<ShoppingItem> GetPagePurchaseItems(ShopperPage page)
        => ShoppingListPolicy.PurchaseItems(items, page, categorySettings.IsAutomaticCategoryEnabled,
            plugin.PurchaseSuppressionIds, plugin.Configuration.EnableHighQualityPurchases);
    private void BeginBuyAll(ShoppingItem[] purchaseItems)
    {
        if (shoppingController.Start(purchaseItems))
        {
            buyAllRunActive = true;
        }
        status = shoppingController.Status;
    }

    private string GetCategoryMissingDisplay(ShoppingCategory category)
        => collectionLoaded
                ? collectionDataSession.State.CompleteFor(category)
                    ? items.Count(item => item.Category == category && IsMissing(item)).ToString("N0", CultureInfo.CurrentCulture)
                    : "Unknown"
                : "—";

    private bool IsPageOwnershipReady(ShopperPage page)
        => Enum.GetValues<ShoppingCategory>().Where(category => PageIncludesCategory(page, category))
            .All(collectionDataSession.State.CompleteFor);

    private static bool PageIncludesCategory(ShopperPage page, ShoppingCategory category)
        => ShoppingProjection.IncludesPage(page, category);

    private bool ShouldShowShoppingStatus()
        => ShoppingListPolicy.ShowStatus(refreshActivity.IsActive, shoppingController.IsRunning, items.Length, status);
    private void DrawAutomaticPurchaseList()
    {
        if (shoppingController.IsRunning)
        {
            ImGui.TextWrapped(shoppingController.Status);
            if (!string.IsNullOrWhiteSpace(shoppingController.CurrentItemName))
                ImGui.TextDisabled(shoppingController.CurrentItemName);
            if (shoppingController.InitialCount > 0)
            {
                var finished = shoppingController.InitialCount - shoppingController.RemainingCount;
                ImGui.ProgressBar(
                    (float)finished / shoppingController.InitialCount,
                    new Vector2(-1, 0),
                    $"{finished} / {shoppingController.InitialCount} - {shoppingController.Spent:N0} gil");
            }
            if (ImGui.Button("Cancel Automated Purchasing"))
                shoppingController.Stop();
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.Text, TextMuted);
        ImGui.TextWrapped("Tab: show category. Purchases: include in All Missing Buy All.");
        ImGui.PopStyleColor();
        categorySettings.Draw();

        DrawCollectionEstimate();
    }

    private MarketPrice? GetPreferredPrice(ShoppingItem item)
        => ShoppingProjection.PreferredPrice(
            item,
            plugin.Configuration.EnableHighQualityPurchases);

    private void DrawCollectionEstimate()
    {
        if (items.Length == 0)
            return;

        if (!Enum.GetValues<ShoppingCategory>().Where(categorySettings.IsAutomaticCategoryEnabled).All(collectionDataSession.State.CompleteFor))
        {
            ImGui.TextWrapped(collectionDataSession.State.StatusDetail);
            return;
        }

        if (!Enum.GetValues<ShoppingCategory>().Any(categorySettings.IsAutomaticCategoryEnabled))
        {
            PluginControls.DrawWrappedNote("Select a purchase category.");
            return;
        }

        var remainingCount = 0;
        var pricedCount = 0;
        var estimate = 0UL;
        foreach (var item in items)
        {
            if (!categorySettings.IsAutomaticCategoryEnabled(item.Category)
                || !IsMissing(item))
            {
                continue;
            }

            remainingCount++;
            if (GetPreferredPrice(item) is { } price)
            {
                pricedCount++;
                estimate += price.PricePerUnit;
            }
        }
        var missingPrices = remainingCount - pricedCount;

        ImGui.TextWrapped($"Estimated total: {estimate:N0} gil (before tax)");
        var marketName = priceMarket ?? "current world";
        PluginControls.DrawWrappedNote(missingPrices == 0
            ? $"{marketName}: {pricedCount:N0} priced"
            : $"{marketName}: {pricedCount:N0} priced; "
                + $"{missingPrices:N0} unpriced");
    }

    private ShoppingItem[] GetVisibleItems(ShopperPage page)
        => visibleItemsCache.Get(new VisibleShoppingItemsCache.Key(
            itemsRevision, completionRevision, page, ShoppingListText.ReadBuffer(filterBuffer),
            plugin.Configuration.HideLocallyOwned, plugin.Configuration.EnableHighQualityPurchases,
            shoppingController.CurrentItemId), items, plugin.PurchaseSuppressionIds);
    private bool HasMissing(ShoppingCategory category)
        => items.Any(item => item.Category == category && IsMissing(item));

    private bool IsMissing(ShoppingItem item)
        => ShoppingListPolicy.IsMissing(item, plugin.PurchaseSuppressionIds);

    private void EnsureActivePageAvailable()
    {
        if (!collectionLoaded)
            return;

        ActivePage = ShopperPagePolicy.KeepVisible(ActivePage, categorySettings.IsCategoryTabVisible);
    }

    private void StartRefresh()
    {
        refreshRequests.RequestCatalog(IsRefreshBusy);
        TryStartRequestedMarketPriceRefresh();
    }

    private bool IsRefreshBusy => refreshActivity.IsActive || refreshOperation.IsRunning;

    private void BeginCollectionRefresh()
    {
        if (disposed || !refreshActivity.TryBegin())
            return;

        var currentContentId = plugin.CurrentContentId;
        var sessionVersion = collectionDataSession.Begin(currentContentId);
        if (!refreshOperation.TryStart(token => refreshActivity.RunAsync(async operationToken =>
            {
                await framework.Run(
                    () => RefreshAsync(
                        currentContentId,
                        sessionVersion,
                        operationToken),
                    operationToken).ConfigureAwait(false);
            }, token)))
        {
            refreshActivity.AbortBegin();
            refreshRequests.RequestCatalog(false);
        }
    }

    private void StartMarketPriceRefresh()
    {
        var currentContentId = plugin.CurrentContentId;
        var sessionVersion = collectionDataSession.Begin(currentContentId);
        var currentWorld = playerState.IsLoaded
            ? playerState.CurrentWorld.ValueNullable?.Name.ToString()
            : null;
        var market = MarketWorldPolicy.Select(currentWorld);
        var itemIds = ShoppingProjection.PriceItemIds(
            ShoppingCatalogProjection.Project(candidates, plugin.ReadCollectionDataOwnership(), prices),
            plugin.PurchaseSuppressionIds);
        if (market is null || itemIds.Length == 0 || !refreshActivity.TryBegin())
            return;

        status = $"Updating {itemIds.Length:N0} current prices on {market}...";
        if (!refreshOperation.TryStart(token => refreshActivity.RunAsync(
                operationToken => RefreshMarketPricesAsync(
                    currentContentId,
                    sessionVersion,
                    market,
                    itemIds,
                    operationToken),
                token)))
        {
            refreshActivity.AbortBegin();
        }
    }

    private Task RefreshMarketPricesAsync(
        ulong currentContentId,
        int sessionVersion,
        string market,
        uint[] itemIds,
        CancellationToken operationToken)
        => RefreshPublication.RunAsync(
            token => client.GetPriceResultsAsync(market, itemIds, token),
            (action, token) => framework.Run(action, token),
            () => !disposed && collectionDataSession.IsCurrent(currentContentId, sessionVersion),
            priceResult =>
            {
                var publication = MarketPricePublication.Validate(market,
                    playerState.IsLoaded ? playerState.CurrentWorld.ValueNullable?.Name.ToString() : null,
                    prices);
                prices = publication.RequestRefresh ? publication.Prices
                    : MarketPricePublication.Merge(priceResult, market, priceMarket, prices);
                items = items.Select(item => item with { Prices = prices.GetValueOrDefault(item.ItemId) }).ToArray();
                itemsRevision++;
                priceMarket = publication.Market;
                if (publication.RequestRefresh)
                {
                    RequestMarketPriceRefresh();
                    return;
                }
                status = priceResult.FailedItemIds.Count == 0 ? $"Current prices refreshed on {market}." : $"Prices partially refreshed on {market}; {priceResult.FailedItemIds.Count:N0} items could not be updated.";
            },
            ex => log.Warning(ex, "Could not refresh Universalis prices when the Market Board opened."),
            () => status = "Current prices could not be updated; keeping the previous estimates.",
            operationToken);
    private async Task RefreshAsync(
        ulong currentContentId,
        int sessionVersion,
        CancellationToken operationToken)
    {
        if (disposed || !collectionDataSession.IsCurrent(currentContentId, sessionVersion))
            return;
        status = "Loading local outfits and public unlock catalogs...";
        try
        {
            CollectionCatalogResult? result = null;
            string? refreshWarning = null;
            remoteWarning = null;
            try
            {
                result = await client.GetCollectionsAsync(operationToken);
                operationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                refreshWarning = "The public FFXIV Collect catalog was unavailable; showing outfits and weapons only.";
                log.Warning(ex, "Could not refresh public FFXIV Collect catalogs; continuing with local catalogs.");
            }

            if (!TryPublishOwnership(currentContentId, sessionVersion))
            {
                return;
            }
            var itemSheet = dataManager.GetExcelSheet<Item>();
            var collectionMappings = CollectionMappingCatalog.Read(dataManager);
            var cabinetSheet = dataManager.GetExcelSheet<Cabinet>();
            var armoireItemIds = cabinetSheet
                .Where(row => row.RowId != 0 && row.Item.RowId != 0)
                .Select(row => row.Item.RowId)
                .ToHashSet();
            bool IsMarketEligible(uint itemId)
                => itemSheet.TryGetRow(itemId, out var item) && CatalogEligibilityPolicy.IsMarketEligible(true, item.IsUntradable, item.ItemSearchCategory.RowId);
            catalogOutfits = OutfitCatalog.LoadBundled();
            var outfitCandidates = catalogOutfits
                .SelectMany(outfit => outfit.Items.Where(piece => piece.Id != 0 && IsMarketEligible(piece.Id))
                    .DistinctBy(piece => piece.Id).Select(piece => new ShoppingCandidate(
                    piece.Id,
                    itemSheet.GetRow(piece.Id).Name.ToString(),
                    itemSheet.GetRow(piece.Id).Icon,
                    outfit.Id,
                    outfit.Name,
                    CatalogEligibilityPolicy.GameAssetPath(outfit.Icon),
                    outfit.Patch,
                    outfit.Sources.Count == 0 ? "Unknown source" : outfit.Sources[0].Text,
                    ShoppingCategory.Outfit,
                    0,
                    0)));

            // FFXIV Collect does not expose a general weapon-glamour collection. Build
            // that part of the list from the native catalog and admit only combat arms
            // or shields that can be stored as glamour or are explicitly in the Armoire.
            var weaponCandidates = itemSheet
                .Where(item => item.RowId != 0 && CatalogEligibilityPolicy.IsWeaponEligible(item.RowId, item.ItemUICategory.Value.OrderMajor, item.ItemUICategory.RowId, item.IsGlamorous, armoireItemIds.Contains(item.RowId), item.IsUntradable, item.ItemSearchCategory.RowId))
                .Select(item => new ShoppingCandidate(
                    item.RowId,
                    item.Name.ToString(),
                    item.Icon,
                    item.ItemUICategory.RowId,
                    $"Weapons — {item.ItemUICategory.Value.Name}",
                    null,
                    string.Empty,
                    armoireItemIds.Contains(item.RowId)
                        ? "Armoire-eligible weapon"
                        : "Glamour Dresser-eligible weapon",
                    ShoppingCategory.Weapon,
                    1,
                    1));

            IEnumerable<ShoppingCandidate> BuildCollectableCandidates(
                IEnumerable<Collectable> collectables,
                ShoppingCategory category,
                int priority)
            {
                foreach (var collectable in collectables)
                {
                    if (collectable.Tradeable is not true
                        || collectable.ItemId is not { } itemId
                        || itemId == 0
                        || !itemSheet.TryGetRow(itemId, out var item)
                        || item.IsUntradable
                        || item.ItemSearchCategory.RowId == 0)
                    {
                        continue;
                    }

                    yield return new ShoppingCandidate(
                        itemId,
                        item.Name.ToString(),
                        item.Icon,
                        collectable.Id,
                        collectable.Name,
                        CatalogEligibilityPolicy.GameAssetPath(collectable.Image ?? collectable.Icon),
                        collectable.Patch,
                        collectable.Sources.Count == 0
                            ? "Unknown source"
                            : collectable.Sources[0].Text,
                        category,
                        priority,
                        1,
                        collectable.Id != 0 && collectionMappings[category].Contains(collectable.Id));
                }
            }

            var collectionPriority = 2;
            var minionCandidates = BuildCollectableCandidates(
                result?.Minions ?? [], ShoppingCategory.Minion, collectionPriority++);
            var mountCandidates = BuildCollectableCandidates(
                result?.Mounts ?? [], ShoppingCategory.Mount, collectionPriority++);
            var orchestrionCandidates = BuildCollectableCandidates(
                result?.Orchestrions ?? [], ShoppingCategory.Orchestrion, collectionPriority++);
            var hairstyleCandidates = BuildCollectableCandidates(
                result?.Hairstyles ?? [], ShoppingCategory.Hairstyle, collectionPriority++);
            var facewearCandidates = BuildCollectableCandidates(
                result?.Facewear ?? [], ShoppingCategory.Facewear, collectionPriority++);
            var fashionCandidates = BuildCollectableCandidates(
                result?.Fashions ?? [], ShoppingCategory.Fashion, collectionPriority);

            catalogCandidates = outfitCandidates
                .Concat(weaponCandidates)
                .Concat(minionCandidates)
                .Concat(mountCandidates)
                .Concat(orchestrionCandidates)
                .Concat(hairstyleCandidates)
                .Concat(facewearCandidates)
                .Concat(fashionCandidates)
                .ToArray();
            RebuildCandidates(false);

            var currentWorld = playerState.IsLoaded
                ? playerState.CurrentWorld.ValueNullable?.Name.ToString()
                : null;
            var market = MarketWorldPolicy.Select(currentWorld);
            IReadOnlyDictionary<uint, MarketPriceQuote> refreshedPrices;
            if (market is null)
            {
                refreshedPrices = new Dictionary<uint, MarketPriceQuote>();
            }
            else
            {
                var priceOwnership = plugin.ReadCollectionDataOwnership();
                if (priceOwnership.Snapshot is null)
                    RequestMarketPriceRefresh();
                var priceCandidateIds = ShoppingProjection.PriceItemIds(
                    ShoppingCatalogProjection.Project(candidates, priceOwnership, prices),
                    plugin.PurchaseSuppressionIds);
                if (priceCandidateIds.Length == 0)
                {
                    refreshedPrices = new Dictionary<uint, MarketPriceQuote>();
                }
                else
                {
                    // Publish the ownership-backed list before optional market-price I/O.
                    // Universalis can be slow or unavailable; it must never prevent local
                    // outfits and weapons from becoming usable.
                    var previousPrices = priceMarket == market ? prices : new Dictionary<uint, MarketPriceQuote>();
                    remoteWarning = refreshWarning;
                    ApplyItems();
                    collectionLoaded = true;
                    status = $"Collection ready. Checking {priceCandidateIds.Length:N0} current prices on {market}...";
                    try
                    {
                        var priceResult = await client.GetPriceResultsAsync(
                            market,
                            priceCandidateIds,
                            operationToken);
                        refreshedPrices = priceResult.MergePrevious(previousPrices);
                        if (priceResult.FailedItemIds.Count != 0)
                            refreshWarning = $"Prices for {priceResult.FailedItemIds.Count:N0} items could not be updated; available estimates are shown.";
                    }
                    catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        refreshedPrices = new Dictionary<uint, MarketPriceQuote>();
                        refreshWarning = refreshWarning is null
                            ? "Current prices are unavailable; the shopping list is still ready."
                            : refreshWarning + " Current prices are also unavailable.";
                        log.Warning(ex, "Could not refresh Universalis prices; keeping the ownership-backed shopping list.");
                    }
                }
            }
            operationToken.ThrowIfCancellationRequested();
            if (!collectionDataSession.IsCurrent(currentContentId, sessionVersion))
            {
                return;
            }
            var publication = MarketPricePublication.Validate(market,
                playerState.IsLoaded ? playerState.CurrentWorld.ValueNullable?.Name.ToString() : null, refreshedPrices);
            if (publication.RequestRefresh)
                RequestMarketPriceRefresh();
            prices = publication.Prices;
            market = publication.Market;
            remoteWarning = refreshWarning;
            ApplyItems();
            priceMarket = market;
            collectionLoaded = true;
            status = BuildLoadedStatus();
        }
        catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
        {
            if (!disposed && collectionDataSession.IsCurrent(currentContentId, sessionVersion))
                status = "Refresh cancelled because the plugin is unloading.";
        }
        catch (Exception ex)
        {
            if (!disposed && collectionDataSession.IsCurrent(currentContentId, sessionVersion))
            {
                log.Error(ex, "Could not build the Collection Shopper list.");
                status = $"Refresh failed: {ex.Message}";
            }
        }
    }

    private void PublishCollectionDataOwnership(
        ulong currentContentId,
        int sessionVersion,
        CancellationToken operationToken)
    {
        operationToken.ThrowIfCancellationRequested();
        if (!TryPublishOwnership(currentContentId, sessionVersion))
        {
            return;
        }

        RebuildCandidates(true);
        ApplyItems();
        if (!refreshActivity.IsActive)
            status = collectionLoaded
                ? BuildLoadedStatus()
                : "Exportaramus ownership refreshed. Run Refresh to rebuild the shopping list.";
    }

    private bool TryPublishOwnership(
        ulong currentContentId,
        int sessionVersion)
    {
        if (disposed || !collectionDataSession.IsCurrent(currentContentId, sessionVersion))
            return false;
        if (currentContentId == 0)
        {
            collectionDataSession.Invalidate();
        }
        else if (!collectionDataSession.TryPublish(currentContentId, sessionVersion,
            plugin.ReadCollectionDataOwnership()))
        {
            return false;
        }

        return true;
    }

    private void RebuildCandidates(bool preserveDisplayed)
        => candidates = ShoppingCatalogProjection.BuildCandidates(
            catalogOutfits,
            catalogCandidates,
            collectionDataSession.State.TrustedOwnedItemIds.ToHashSet(),
            plugin.PurchaseSuppressionIds,
            preserveDisplayed ? candidates : []);

    private void ApplyItems()
    {
        items = ShoppingCatalogProjection.Project(candidates, collectionDataSession.State, prices);
        itemsRevision++;
        PrepareTextureCaches();
        EnsureActivePageAvailable();
    }

    private string BuildLoadedStatus()
    {
        return remoteWarning ?? "Collection refreshed.";
    }



}

