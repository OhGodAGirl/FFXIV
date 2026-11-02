using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SpringCleaning.Shared.CollectionData;

namespace SpringCleaning;

internal sealed partial class MigrationWindow : Window, IDisposable
{
    private readonly MigrationController controller;
    private readonly DresserScanner scanner;
    private readonly DuplicateDiscardController duplicateDiscardController;
    private readonly DresserCleanupController cleanup;
    private readonly Configuration configuration;
    private readonly ITextureProvider textureProvider;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly Action configurationChanged;
    private readonly Action<TimeSpan> requestPreviewRefresh;
    private readonly IFontHandle titleFont;
    private readonly IFontHandle sectionFont;
    private IReadOnlyList<InventoryDresserOutfit> inventoryDresserCandidates = [];
    private IReadOnlyList<CabinetInventoryItem> inventoryArmoireCandidates = [];
    private IReadOnlyList<EligibleDresserItem> dresserArmoireCandidates = [];
    private IReadOnlyList<InventoryDuplicateItem> inventoryDuplicates = [];
    private Vector2? dockedPosition;
    private Vector2? dockedSize;
    private bool stylesPushed;
    private bool closeRequested;
    private bool dockedToDresser;
    private bool placementApplied;
    private bool? footerEnabled;
    private bool pagePresentationReady;
    private HashSet<(int Index, uint RawId)> restorableDuplicates = [];
    private bool RetainPage => controller.RetainPresentationDuringRefresh && pagePresentationReady;
    private bool hiddenByNativeWindow;
    internal NativeWindowOcclusionPolicy.Region? DrawBounds { get; private set; }

    private bool inputBlockedByNativeWindow;
    internal void SetNativeOcclusion(bool occluded, bool inputBlocked = false)
    {
        hiddenByNativeWindow = occluded;
        inputBlockedByNativeWindow = occluded || inputBlocked;
    }

    public SpringCleaningPage ActivePage { get; private set; } = SpringCleaningPage.Duplicates;

    public MigrationWindow(
                MigrationController controller,
                DresserScanner scanner,
                DuplicateDiscardController duplicateDiscardController,
                DresserCleanupController cleanup,
                Configuration configuration,
                ITextureProvider textureProvider,
                IDalamudPluginInterface pluginInterface,
                Action configurationChanged,
                Action<TimeSpan> requestPreviewRefresh)
                : base(
                    "Spring Cleaning##SpringCleaning",
                    ImGuiWindowFlags.NoTitleBar
                    | ImGuiWindowFlags.NoCollapse
                    | ImGuiWindowFlags.NoResize
                    | ImGuiWindowFlags.NoScrollbar
                    | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.controller = controller;
        this.scanner = scanner;
        this.duplicateDiscardController = duplicateDiscardController;
        this.cleanup = cleanup;
        this.configuration = configuration;
        this.textureProvider = textureProvider;
        this.pluginInterface = pluginInterface;
        this.configurationChanged = configurationChanged;
        this.requestPreviewRefresh = requestPreviewRefresh;
        using (pluginInterface.UiBuilder.FontAtlas.SuppressAutoRebuild())
        {
            titleFont = pluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(
                new GameFontStyle(GameFontFamily.TrumpGothic, 23f));
            sectionFont = pluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(
                new GameFontStyle(GameFontFamily.MiedingerMid, 17f));
        }
        IsOpen = false;
        Size = new Vector2(360f, 650f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(260f, 260f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public int InventoryDresserCandidateCount => inventoryDresserCandidates.Sum(outfit => outfit.Components.Count);

    public int InventoryArmoireCandidateCount => inventoryArmoireCandidates.Count;

    public int DresserArmoireCandidateCount => dresserArmoireCandidates.Count;

    public void Dispose()
    {
        duplicateReviewCache.Clear();
        cleanup.Cancel();
        duplicateDiscardController.CancelAll();
        titleFont.Dispose();
        sectionFont.Dispose();
    }

    public void AdvanceDiscardWorkflow()
    {
        cleanup.Update();
        duplicateDiscardController.Update();
    }

    public bool ConsumeCloseRequest()
    {
        var requested = closeRequested;
        closeRequested = false;
        return requested;
    }

    public void OpenPage(SpringCleaningPage page)
    {
        footerEnabled = null;
        pagePresentationReady = false;
        ActivePage = page;
        requestPreviewRefresh(TimeSpan.Zero);
        IsOpen = true;
    }

    public void ClearPresentation()
    {
        duplicateReviewCache.Clear();
        footerEnabled = null;
        pagePresentationReady = false;
    }

    public void UpdateDresserArmoireCandidates(
        IReadOnlyList<EligibleDresserItem> updatedCandidates)
    {
        ArgumentNullException.ThrowIfNull(updatedCandidates);
        dresserArmoireCandidates = updatedCandidates;
    }

    public void UpdateInventoryDresserCandidates(
        IReadOnlyList<InventoryDresserOutfit> updatedCandidates)
    {
        ArgumentNullException.ThrowIfNull(updatedCandidates);
        inventoryDresserCandidates = updatedCandidates;
    }

    public void UpdateInventoryArmoireCandidates(
        IReadOnlyList<CabinetInventoryItem> updatedCandidates)
    {
        ArgumentNullException.ThrowIfNull(updatedCandidates);
        inventoryArmoireCandidates = updatedCandidates;
    }

    public void UpdateDuplicateCandidates(
        IReadOnlyList<InventoryDuplicateItem> updatedCandidates)
    {
        ArgumentNullException.ThrowIfNull(updatedCandidates);
        inventoryDuplicates = updatedCandidates;
        restorableDuplicates = controller.Alerts.Where(cleanup.CanRestore)
            .Select(entry => (entry.PrismBoxIndex, entry.RawItemId)).ToHashSet();
    }

    public void MatchDresser(Vector2 position, Vector2 size)
    {
        dockedSize = size;
        if (configuration.RememberWindowPlacement && placementApplied)
            return;
        dockedPosition = position;
        dockedToDresser = true;
        placementApplied = true;
    }

    public void BeginDresserSession(bool snapToDefault)
    {
        dockedPosition = null;
        dockedSize = null;
        dockedToDresser = false;
        placementApplied = !snapToDefault;
    }

    public void ReleaseDresserDock()
    {
        hiddenByNativeWindow = false;
        inputBlockedByNativeWindow = false;
        dockedPosition = null;
        dockedSize = null;
        dockedToDresser = false;
    }

    public override void PreDraw()
    {
        Flags = inputBlockedByNativeWindow ? Flags | ImGuiWindowFlags.NoInputs : Flags & ~ImGuiWindowFlags.NoInputs;
        if (dockedPosition is null && DrawBounds is { } bounds)
        {
            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(DrawerPlacementPolicy.ClampPosition(bounds.Minimum, bounds.Size,
                viewport.WorkPos, viewport.WorkSize), ImGuiCond.Always);
        }
        if (dockedPosition is { } position)
        {
            ImGui.SetNextWindowPos(position, ImGuiCond.Always);
        }
        if (dockedSize is { } size)
        {
            // WindowHost applies Size after PreDraw; leave explicit dock sizing authoritative.
            Size = null;
            ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        }
        if (configuration.RememberWindowPlacement)
        {
            dockedPosition = null;
            dockedSize = null;
        }
        if (dockedPosition is null && dockedSize is null)
            dockedToDresser = false;

        SpringCleaningUi.PushMainWindowStyle();
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, hiddenByNativeWindow ? 0f : ImGui.GetStyle().Alpha);
        stylesPushed = true;
    }

    public override void PostDraw()
    {
        if (!stylesPushed)
            return;

        ImGui.PopStyleVar();
        SpringCleaningUi.PopMainWindowStyle();
        stylesPushed = false;
    }

    public UiDrawObservation DrawObservation { get; private set; }

    public override void Draw()
    {
        DrawObservation = UiDrawObservation.Capture();
        DrawBounds = new(ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize());
        if (hiddenByNativeWindow)
            return;
        try
        {
            DrawContents();
        }
        finally
        {
            DrawGildedFrame();
            DrawBounds = new(ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize());
        }
    }

    private void DrawContents()
    {
        if (!controller.RetainPresentationDuringRefresh)
            pagePresentationReady = (ActivePage != SpringCleaningPage.Settings || cleanup.IsRunning)
                && controller.HasFreshDuplicateOwnership;
        DrawTitleBar();

        if (controller.IsActive)
        {
            DrawActiveMigration();
            return;
        }

        if (cleanup.IsRunning)
        {
            DrawDrawerDuplicates();
            return;
        }

        if (ActivePage == SpringCleaningPage.Settings)
        {
            ImGui.BeginChild("##DrawerSettings", Vector2.Zero, false);
            SettingsPanel.Draw(configuration, configurationChanged);
            ImGui.EndChild();
        }
        else if (ActivePage == SpringCleaningPage.Duplicates)
        {
            DrawDrawerDuplicates();
        }
        else
        {
            DrawDrawerDuplicates();
            if (controller.Phase != MigrationPhase.Idle)
                DrawPageStatus();
        }
    }

    private void DrawInventoryToDresserPage()
    {
        var outfitCount = inventoryDresserCandidates.Count;
        var pieceCount = inventoryDresserCandidates.Sum(outfit => outfit.Components.Count);
        DrawPageHeading(
            "Inventory to Dresser",
            scanner.AreDresserInventoryCandidatesReady
                ? $"{outfitCount:N0} outfit{(outfitCount == 1 ? string.Empty : "s")}"
                : "Scanning");
        DrawDisabledWrapped("Move safe outfit pieces from your inventory into the Glamour Dresser.");

        ImGui.Spacing();
        var onlyCompleteOutfits = configuration.OnlyMoveCompleteOutfits;
        if (ImGui.Checkbox("Only Move Complete Outfits", ref onlyCompleteOutfits))
        {
            configuration.OnlyMoveCompleteOutfits = onlyCompleteOutfits;
            configurationChanged();
            requestPreviewRefresh(TimeSpan.Zero);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Skip an outfit unless every defined piece is safely available in your inventory.");

        ImGui.Spacing();
        DrawInventoryDresserTable();
        var prismText = controller.IsDresserPrismCountReady
            ? $"Uses up to {controller.DresserPrismsRequired:N0} Prism"
                + (controller.DresserPrismsRequired == 1 ? string.Empty : "s")
            : "Prism balance verified by FFXIV during storage";
        DrawPageFooter(
            $"{pieceCount:N0} piece{(pieceCount == 1 ? string.Empty : "s")} · {prismText}",
            "StoreInventoryInDresser",
            "Store Outfits in Dresser",
            !controller.CanStartInventoryDresser,
            controller.StartInventoryDresserStore);
    }

    private void DrawInventoryToArmoirePage()
    {
        var count = inventoryArmoireCandidates.Count;
        DrawPageHeading(
            "Inventory to Armoire",
            scanner.IsCabinetLoaded
                ? $"{count:N0} eligible item{(count == 1 ? string.Empty : "s")}"
                : "Armoire not loaded");
        DrawDisabledWrapped("Move eligible outfit pieces directly from your inventory into the Armoire.");
        ImGui.Spacing();
        DrawInventoryArmoireTable();
        DrawPageFooter(
            scanner.IsCabinetLoaded
                ? $"{count:N0} item{(count == 1 ? string.Empty : "s")} ready"
                : "Spring Cleaning will load the Armoire before storing",
            "StoreInventoryInArmoire",
            "Store Outfits in Armoire",
            !controller.CanStartInventoryArmoire,
            controller.StartInventoryArmoireStore);
    }

    private void DrawDresserToArmoirePage()
    {
        var count = dresserArmoireCandidates.Count;
        DrawPageHeading(
            "Dresser to Armoire",
            $"{count:N0} eligible entr{(count == 1 ? "y" : "ies")}");
        DrawDisabledWrapped("These Glamour Dresser entries can be moved into the Armoire.");
        ImGui.Spacing();
        DrawDresserArmoireTable();
        DrawPageFooter(
            $"{count:N0} entr{(count == 1 ? "y" : "ies")} ready",
            "MoveDresserToArmoire",
            "Move Eligible Items to Armoire",
            !controller.CanStart || count == 0,
            controller.Start);
    }

    private void DrawInventoryDresserTable()
    {
        ImGui.BeginChild("##InventoryDresserList", GetListSize(), true);
        if (!controller.HasFreshInventoryDresserOwnership && !RetainPage)
        {
            DrawEmptyState(controller.OwnershipReadiness(CollectionSourceName.Inventory, CollectionSourceName.GlamourDresser));
            ImGui.EndChild();
            return;
        }
        if (!scanner.AreDresserInventoryCandidatesReady)
        {
            DrawEmptyState("Waiting for the Glamour Dresser's eligible-item list.");
            ImGui.EndChild();
            return;
        }
        if (inventoryDresserCandidates.Count == 0)
        {
            DrawEmptyState(configuration.OnlyMoveCompleteOutfits
                ? "No complete inventory outfits are ready for the Glamour Dresser."
                : "No inventory outfit pieces are ready for the Glamour Dresser.");
            ImGui.EndChild();
            return;
        }

        if (ImGui.BeginTable(
                "##InventoryDresserCandidates",
                2,
                ImGuiTableFlags.RowBg
                | ImGuiTableFlags.BordersInnerH
                | ImGuiTableFlags.ScrollY
                | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Outfit", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Pieces", ImGuiTableColumnFlags.WidthFixed, 90f);
            ImGui.TableHeadersRow();
            foreach (var outfit in inventoryDresserCandidates)
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.None, 48f);
                ImGui.TableSetColumnIndex(0);
                var firstPiece = outfit.Components.Count > 0 ? outfit.Components[0] : null;
                if (firstPiece is not null)
                {
                    DrawGameIcon(firstPiece.IconId, firstPiece.IsHighQuality, 36f);
                    ImGui.SameLine();
                }
                CenterRowText(outfit.Name, 48f);
                ImGui.TableSetColumnIndex(1);
                CenterRowText($"{outfit.Components.Count:N0}", 48f);
            }
            ImGui.EndTable();
        }
        ImGui.EndChild();
    }

    private void DrawInventoryArmoireTable()
    {
        ImGui.BeginChild("##InventoryArmoireList", GetListSize(), true);
        if (!controller.HasFreshInventoryArmoireOwnership && !RetainPage)
        {
            DrawEmptyState(controller.OwnershipReadiness(CollectionSourceName.Inventory, CollectionSourceName.Armoire));
            ImGui.EndChild();
            return;
        }
        if (!scanner.IsCabinetLoaded)
        {
            DrawEmptyState("Open the Armoire once to preview eligible inventory items here.");
            ImGui.EndChild();
            return;
        }
        if (inventoryArmoireCandidates.Count == 0)
        {
            DrawEmptyState("No carried items are currently eligible for the Armoire.");
            ImGui.EndChild();
            return;
        }

        if (ImGui.BeginTable(
                "##InventoryArmoireCandidates",
                2,
                ImGuiTableFlags.RowBg
                | ImGuiTableFlags.BordersInnerH
                | ImGuiTableFlags.ScrollY
                | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Storage", ImGuiTableColumnFlags.WidthFixed, 120f);
            ImGui.TableHeadersRow();
            foreach (var item in inventoryArmoireCandidates)
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.None, 48f);
                ImGui.TableSetColumnIndex(0);
                DrawGameIcon(item.IconId, item.IsHighQuality, 36f);
                ImGui.SameLine();
                CenterRowText(item.Name + (item.IsHighQuality ? "  HQ" : string.Empty), 48f);
                ImGui.TableSetColumnIndex(1);
                CenterRowText("Armoire", 48f);
            }
            ImGui.EndTable();
        }
        ImGui.EndChild();
    }

    private void DrawDresserArmoireTable()
    {
        ImGui.BeginChild("##DresserArmoireList", GetListSize(), true);
        if (!controller.HasFreshDresserArmoireOwnership && !RetainPage)
        {
            DrawEmptyState(controller.OwnershipReadiness(CollectionSourceName.GlamourDresser, CollectionSourceName.Armoire));
            ImGui.EndChild();
            return;
        }
        if (dresserArmoireCandidates.Count == 0)
        {
            DrawEmptyState("Nothing in the Glamour Dresser currently needs moving.");
            ImGui.EndChild();
            return;
        }

        if (ImGui.BeginTable(
                "##DresserArmoireCandidates",
                2,
                ImGuiTableFlags.RowBg
                | ImGuiTableFlags.BordersInnerH
                | ImGuiTableFlags.ScrollY
                | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Dresser Entry", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Contents", ImGuiTableColumnFlags.WidthFixed, 140f);
            ImGui.TableHeadersRow();
            foreach (var item in dresserArmoireCandidates)
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.None, 48f);
                ImGui.TableSetColumnIndex(0);
                var alert = controller.Alerts.FirstOrDefault(entry => entry.PrismBoxIndex == item.PrismBoxIndex);
                if (alert is not null)
                {
                    DrawGameIcon(alert.IconId, alert.IsHighQuality, 36f);
                    ImGui.SameLine();
                }
                CenterRowText(item.Name, 48f);
                ImGui.TableSetColumnIndex(1);
                CenterRowText(
                    item.IsOutfit
                        ? $"{item.Components.Count:N0} outfit pieces"
                        : "Single item",
                    48f);
            }
            ImGui.EndTable();
        }
        ImGui.EndChild();
    }

    private static Vector2 GetListSize()
    {
        return new Vector2(0f, MathF.Max(130f, ImGui.GetContentRegionAvail().Y - 58f));
    }

    private static void DrawEmptyState(string message)
    {
        var start = ImGui.GetCursorPos();
        var available = ImGui.GetContentRegionAvail();
        var textSize = ImGui.CalcTextSize(message);
        ImGui.SetCursorPos(start + new Vector2(
            MathF.Max(0f, (available.X - textSize.X) * 0.5f),
            MathF.Max(0f, (available.Y - textSize.Y) * 0.45f)));
        ImGui.TextDisabled(message);
    }

    private void DrawGameIcon(uint iconId, bool highQuality, float size)
    {
        var lookup = new GameIconLookup(iconId, highQuality);
        var texture = textureProvider.GetFromGameIcon(in lookup).GetWrapOrEmpty();
        ImGui.Image(texture.Handle, new Vector2(size));
    }

    private static void CenterRowText(string text, float rowHeight)
    {
        var textHeight = ImGui.CalcTextSize(text).Y;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + MathF.Max(0f, (rowHeight - textHeight) * 0.5f));
        ImGui.TextUnformatted(text);
    }

    private void DrawPageHeading(string title, string count)
    {
        ImGui.Spacing();
        using (sectionFont.Push())
        {
            ImGui.PushStyleColor(ImGuiCol.Text, SpringCleaningUi.Gold);
            ImGui.TextUnformatted(title);
            ImGui.PopStyleColor();
        }
        ImGui.SameLine();
        ImGui.TextDisabled(count);
        ImGui.Separator();
    }

    private void DrawPageFooter(
        string summary,
        string buttonId,
        string buttonLabel,
        bool disabled,
        Action action)
    {
        if (!RetainPage || footerEnabled is null)
            footerEnabled = !disabled;
        ImGui.Spacing();
        if (!ImGui.BeginTable("##PageFooter", 2, ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("Summary", ImGuiTableColumnFlags.WidthStretch);
        var actionWidth = MathF.Max(
            260f,
            ImGui.CalcTextSize(buttonLabel).X + (ImGui.GetStyle().FramePadding.X * 4f));
        ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, actionWidth);
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 8f);
        ImGui.TextDisabled(summary);
        ImGui.TableSetColumnIndex(1);
        if (SpringCleaningUi.DrawNativeButton(
                buttonId,
                buttonLabel,
                new Vector2(ImGui.GetContentRegionAvail().X, 36f),
                !footerEnabled.Value) && !disabled)
        {
            action();
        }
        ImGui.EndTable();
    }

    private void DrawPageStatus()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, SpringCleaningUi.TextMuted);
        ImGui.TextWrapped(controller.Status);
        ImGui.PopStyleColor();
    }

    private void DrawActiveMigration()
    {
        var heading = controller.Mode switch
        {
            MigrationMode.InventoryToDresser => "Store Outfits in Dresser",
            MigrationMode.ArmouryToDresser => "Store Armoury Items in Dresser",
            MigrationMode.InventoryToArmoire => "Store Outfits in Armoire",
            MigrationMode.InventoryCleanup => "Store Outfits",
            _ => "Move Outfits to Armoire",
        };
        DrawSectionHeading(heading);

        ImGui.BeginChild(
            "##ActiveMigration",
            new Vector2(0f, MathF.Max(1f, ImGui.GetContentRegionAvail().Y - 50f)),
            true,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.TextWrapped(controller.Status);
        if (!string.IsNullOrWhiteSpace(controller.CurrentItemName))
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, SpringCleaningUi.Gold);
            ImGui.TextUnformatted(controller.CurrentItemName);
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();
        if (controller.TotalPieceCount > 0)
        {
            var progress = (float)controller.StoredCount / controller.TotalPieceCount;
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.13f, 0.135f, 0.13f, 1f));
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.62f, 0.52f, 0.32f, 1f));
            ImGui.ProgressBar(
                progress,
                new Vector2(-1f, 28f),
                $"{controller.StoredCount:N0} / {controller.TotalPieceCount:N0} pieces");
            ImGui.PopStyleColor(2);
        }
        else if (controller.Mode is MigrationMode.InventoryCleanup or MigrationMode.InventoryToArmoire)
        {
            ImGui.TextDisabled("Eligible items will be counted after the Armoire opens.");
        }

        ImGui.EndChild();
        ImGui.Spacing();
        if (SpringCleaningUi.DrawNativeButton(
                "CancelMigration",
                "Cancel",
                new Vector2(ImGui.GetContentRegionAvail().X, 36f),
                false))
        {
            controller.Cancel();
        }
    }

    private void DrawReviewTab(ReviewPlan review)
    {
        DrawPageHeading(
            "Review",
            $"{review.Count:N0} item{(review.Count == 1 ? string.Empty : "s")}"
                + (RetainPage ? " - Refreshing, kupo!" : string.Empty));
        DrawDisabledWrapped("Review stored and carried items that may need attention.");
        ImGui.Spacing();
        var onlyOutsideOutfits = configuration.ReviewOnlyItemsOutsideOutfits;
        if (ImGui.Checkbox("Only Show Dresser Items Outside Outfits", ref onlyOutsideOutfits))
        {
            configuration.ReviewOnlyItemsOutsideOutfits = onlyOutsideOutfits;
            configurationChanged();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Show Glamour Dresser items that are not components of a recognized FFXIV outfit set. "
                + "Select an item to restore it to inventory.");
        }
        if (!string.IsNullOrWhiteSpace(duplicateDiscardController.Status))
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, SpringCleaningUi.TextMuted);
            ImGui.TextWrapped(duplicateDiscardController.Status);
            ImGui.PopStyleColor();
        }
        if (!controller.HasFreshDuplicateOwnership && !RetainPage)
        {
            ImGui.Spacing();
            DrawDisabledWrapped(controller.OwnershipReadiness(CollectionSourceName.Inventory, CollectionSourceName.GlamourDresser, CollectionSourceName.Armoire));
            return;
        }
        if (!configuration.ReviewOnlyItemsOutsideOutfits)
            DrawBulkDiscardControl(review.InventoryDuplicates);
        ImGui.Spacing();
        ImGui.BeginChild("##DuplicateList", Vector2.Zero, true);
        if (review.Count == 0)
        {
            DrawEmptyState(configuration.ReviewOnlyItemsOutsideOutfits
                ? "Every Dresser item belongs to a recognized outfit."
                : "No items need attention.");
            ImGui.EndChild();
            return;
        }

        if (configuration.ReviewOnlyItemsOutsideOutfits)
        {
            DrawAlertEntries("Not in an Outfit", "outside-outfits", review.DresserEntries);
        }
        else
        {
            DrawInventoryDuplicates(review.InventoryDuplicates);
            DrawAlertGroup(
                review.DresserEntries,
                DresserAlertKind.ArmoireEligible,
                "Ready for Armoire");
            DrawAlertGroup(
                review.DresserEntries,
                DresserAlertKind.DuplicateInExistingOutfit,
                "Already present in an outfit");
            DrawAlertGroup(
                review.DresserEntries,
                DresserAlertKind.Duplicate,
                "Duplicate dresser entries");
        }
        ImGui.EndChild();
    }

    private void DrawBulkDiscardControl(IReadOnlyList<InventoryDuplicateItem> duplicates)
    {
        var safeCopies = InventoryDuplicatePlanner.SelectBulkDiscardCandidates(duplicates);
        var running = duplicateDiscardController.IsBulkRunning;
        ImGui.Spacing();
        if (running)
        {
            if (!ImGui.BeginTable(
                    "##BulkDiscardProgress",
                    2,
                    ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings))
            {
                return;
            }

            ImGui.TableSetupColumn("##progress", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("##cancel", ImGuiTableColumnFlags.WidthFixed, 48f);
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            var progress = duplicateDiscardController.BulkTotal == 0
                ? 0f
                : (float)duplicateDiscardController.BulkCompleted
                    / duplicateDiscardController.BulkTotal;
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.13f, 0.135f, 0.13f, 1f));
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.62f, 0.52f, 0.32f, 1f));
            ImGui.ProgressBar(
                progress,
                new Vector2(-1f, 32f),
                $"{duplicateDiscardController.BulkCompleted:N0} / {duplicateDiscardController.BulkTotal:N0}");
            ImGui.PopStyleColor(2);

            ImGui.TableSetColumnIndex(1);
            using (pluginInterface.UiBuilder.IconFontHandle.Push())
            {
                if (SpringCleaningUi.DrawNativeButton(
                        "CancelBulkDiscard",
                        FontAwesomeIcon.Times.ToIconString(),
                        new Vector2(40f, 34f),
                        false,
                        SpringCleaningUi.Gold))
                {
                    duplicateDiscardController.CancelBulk();
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Cancel discarding duplicates.");
            ImGui.EndTable();
            return;
        }

        if (SpringCleaningUi.DrawNativeButton(
                "BulkDiscardSafeCopies",
                "Discard All Duplicates",
                new Vector2(MathF.Min(260f, ImGui.GetContentRegionAvail().X), 32f),
                safeCopies.Length == 0) && controller.HasFreshDuplicateOwnership)
        {
            duplicateDiscardController.StartBulk(safeCopies);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(safeCopies.Length == 0
                ? "No carried duplicates are currently safe to discard."
                : "Discard each safe carried duplicate in sequence. Spring Cleaning handles its own FFXIV confirmations; protected copies are skipped.");
        }
    }

    private void DrawInventoryDuplicates(IReadOnlyList<InventoryDuplicateItem> duplicates)
    {
        if (duplicates.Count == 0)
            return;

        DrawSectionHeading("Carried Duplicates");
        DrawDisabledWrapped(
            "These carried items already have an appearance in the Armoire, "
            + "the Glamour Dresser, or both.");

        ImGui.Spacing();
        if (!ImGui.BeginTable(
                "##CarriedDuplicates",
                2,
                ImGuiTableFlags.RowBg
                | ImGuiTableFlags.BordersInnerH
                | ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 170f);

        foreach (var candidate in duplicates)
        {
            ImGui.PushID($"duplicate-{candidate.InventoryTypeId}-{candidate.SlotIndex}");
            ImGui.TableNextRow();
            var discardActive = duplicateDiscardController.IsActive(candidate);
            var discardQueued = duplicateDiscardController.IsQueued(candidate);
            if (discardActive || discardQueued)
            {
                var pulse = 0.5f + (0.5f * MathF.Sin((float)ImGui.GetTime() * 4f));
                ImGui.TableSetBgColor(
                    ImGuiTableBgTarget.RowBg0,
                    ImGui.GetColorU32(new Vector4(
                        0.44f,
                        0.36f,
                        0.18f,
                        discardActive ? 0.28f + (pulse * 0.12f) : 0.18f + (pulse * 0.06f))));
            }
            ImGui.TableSetColumnIndex(0);
            var lookup = new GameIconLookup(candidate.IconId, candidate.IsHighQuality);
            var texture = textureProvider.GetFromGameIcon(in lookup).GetWrapOrEmpty();
            ImGui.Image(texture.Handle, new Vector2(34f));
            if (discardActive || discardQueued)
                DrawQueuedActionMarker(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), discardActive);
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.TextUnformatted(candidate.Name + (candidate.IsHighQuality ? "  HQ" : string.Empty));

            if (candidate.Disposition == InventoryDuplicateDisposition.DresserFallback)
            {
                if (!candidate.Safety.IsSafe)
                {
                    ImGui.TextColored(
                        SpringCleaningUi.Warning,
                        candidate.Safety.Status);
                }
                else
                    ImGui.TextDisabled("Already stored in the Armoire.");
            }
            else if (candidate.Disposition == InventoryDuplicateDisposition.RemovalReview)
            {
                ImGui.TextDisabled(
                    candidate.Source == CollectionSourceName.ArmoryChest
                        ? "Duplicate found in Armoury Chest"
                        : "Duplicate found in Inventory");
                if (!candidate.Safety.IsSafe)
                {
                    ImGui.TextColored(
                        SpringCleaningUi.Warning,
                        "This carried copy has meaningful item state. Discarding it removes that copy.");
                }
            }
            ImGui.EndGroup();

            ImGui.TableSetColumnIndex(1);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2f);
            if (candidate.Disposition == InventoryDuplicateDisposition.DresserFallback)
            {
                if (SpringCleaningUi.DrawNativeButton(
                        "StoreDuplicateInDresser",
                        "Store in Dresser",
                        new Vector2(160f, 32f),
                        !candidate.Safety.IsSafe) && controller.HasFreshDuplicateOwnership)
                {
                    duplicateDiscardController.OpenStoreMenu(candidate);
                }
            }
            else if (candidate.Disposition == InventoryDuplicateDisposition.RemovalReview
                     && SpringCleaningUi.DrawNativeButton(
                         "DiscardDuplicate",
                         discardActive ? "Discarding…" : discardQueued ? "Queued" : "Discard",
                         new Vector2(160f, 32f),
                         !candidate.Safety.IsPresent
                         || !candidate.Safety.IsSafe
                         || discardActive
                         || discardQueued
                         || duplicateDiscardController.IsAnyRunning)
                     && controller.HasFreshDuplicateOwnership)
            {
                duplicateDiscardController.StartSingle(candidate);
            }
            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private static void DrawQueuedActionMarker(Vector2 iconMin, Vector2 iconMax, bool active)
    {
        var drawList = ImGui.GetWindowDrawList();
        var pulse = 0.5f + (0.5f * MathF.Sin((float)ImGui.GetTime() * 4f));
        var gold = new Vector4(
            0.95f,
            0.76f,
            0.34f,
            active ? 0.72f + (pulse * 0.28f) : 0.56f + (pulse * 0.16f));
        drawList.AddRect(
            iconMin - new Vector2(2f),
            iconMax + new Vector2(2f),
            ImGui.GetColorU32(gold),
            4f,
            ImDrawFlags.RoundCornersAll,
            active ? 2.25f : 1.5f);

        var badgeCenter = iconMax - new Vector2(3f);
        const float badgeRadius = 9f;
        drawList.AddCircleFilled(
            badgeCenter,
            badgeRadius,
            ImGui.GetColorU32(new Vector4(0.11f, 0.105f, 0.09f, 0.98f)),
            20);
        drawList.AddCircle(badgeCenter, badgeRadius, ImGui.GetColorU32(gold), 20, 1.6f);
        var angle = (float)ImGui.GetTime() * (active ? 4f : 2f);
        var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        drawList.AddLine(
            badgeCenter + (direction * 2f),
            badgeCenter + (direction * 6f),
            ImGui.GetColorU32(gold),
            2.2f);
        drawList.AddCircleFilled(badgeCenter, 1.8f, ImGui.GetColorU32(gold), 10);
    }

    private void DrawTitleBar()
    {
        var windowPosition = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var start = windowPosition + new Vector2(5f, 5f);
        var width = windowSize.X - 10f;
        const float height = 34f;
        const float closeWidth = 52f;
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(
            start,
            start + new Vector2(width, height),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.23f, 0.235f, 0.23f, 1f)),
            7f,
            ImDrawFlags.RoundCornersTop);
        drawList.AddRectFilled(
            start + new Vector2(1f, 1f),
            start + new Vector2(width - 1f, 9f),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.32f, 0.325f, 0.315f, 0.72f)),
            6f,
            ImDrawFlags.RoundCornersTop);
        drawList.AddLine(
            start + new Vector2(1f, height - 1f),
            start + new Vector2(width - 1f, height - 1f),
            ImGui.ColorConvertFloat4ToU32(SpringCleaningUi.GoldMuted),
            1.2f);
        drawList.AddLine(
            start + new Vector2(8f, height + 1f),
            start + new Vector2(width - 8f, height + 1f),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.035f, 0.035f, 0.035f, 0.95f)),
            2f);

        ImGui.SetCursorScreenPos(start);
        ImGui.InvisibleButton("##SpringCleaningTitleDrag", new Vector2(width - closeWidth, height));
        if (!dockedToDresser
            && ImGui.IsItemActive()
            && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            var viewport = ImGui.GetMainViewport();
            ImGui.SetWindowPos(DrawerPlacementPolicy.ClampPosition(
                ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta, ImGui.GetWindowSize(),
                viewport.WorkPos, viewport.WorkSize));
        }

        ImGui.SetCursorScreenPos(start + new Vector2(9f, 3f));
        ImGui.TextUnformatted(controller.IsActive ? "Storage"
                : !cleanup.IsRunning && ActivePage == SpringCleaningPage.Settings ? "Settings" : "Duplicates");

        var closeCenter = start + new Vector2(width - 26f, 17f);
        ImGui.SetCursorScreenPos(closeCenter - new Vector2(18f));
        ImGui.InvisibleButton("##SpringCleaningClose", new Vector2(36f));
        var closeHovered = ImGui.IsItemHovered();
        if (ImGui.IsItemClicked())
        {
            controller.Cancel();
            cleanup.Cancel();
            closeRequested = true;
            IsOpen = false;
        }
        DrawCloseButton(drawList, closeCenter, closeHovered);
        if (closeHovered)
            ImGui.SetTooltip("Close");

        ImGui.SetCursorScreenPos(new Vector2(windowPosition.X + 11f, start.Y + height + 7f));
    }

    private static void DrawGildedFrame()
    {
        // The shared window style draws the single outer border.
    }

    private static void DrawCloseButton(ImDrawListPtr drawList, Vector2 center, bool hovered)
    {
        var background = ImGui.ColorConvertFloat4ToU32(hovered
            ? new Vector4(0.46f, 0.42f, 0.31f, 1f)
            : new Vector4(0.25f, 0.255f, 0.24f, 1f));
        var border = ImGui.ColorConvertFloat4ToU32(hovered
            ? SpringCleaningUi.Gold
            : new Vector4(0.76f, 0.68f, 0.49f, 1f));
        var shadow = ImGui.ColorConvertFloat4ToU32(new Vector4(0.03f, 0.03f, 0.028f, 0.95f));
        var cross = ImGui.ColorConvertFloat4ToU32(hovered
            ? Vector4.One
            : new Vector4(0.96f, 0.95f, 0.90f, 1f));

        var minimum = center - new Vector2(14f, 13f);
        var maximum = center + new Vector2(14f, 13f);
        drawList.AddRectFilled(minimum, maximum, background, 4f);
        drawList.AddRect(minimum, maximum, border, 4f, ImDrawFlags.RoundCornersAll, 1f);
        drawList.AddLine(center + new Vector2(-5f, -4f), center + new Vector2(6f, 7f), shadow, 3f);
        drawList.AddLine(center + new Vector2(6f, -4f), center + new Vector2(-5f, 7f), shadow, 3f);
        drawList.AddLine(center + new Vector2(-6f, -6f), center + new Vector2(6f, 6f), cross, 2.2f);
        drawList.AddLine(center + new Vector2(6f, -6f), center + new Vector2(-6f, 6f), cross, 2.2f);
    }

    private void DrawAlertGroup(
        IReadOnlyList<DresserAlertEntry> source,
        DresserAlertKind kind,
        string label,
        Func<DresserAlertEntry, bool>? predicate = null)
    {
        var entries = source
            .Where(entry => entry.Kind == kind && (predicate is null || predicate(entry)))
            .ToArray();
        if (entries.Length == 0)
            return;

        DrawAlertEntries(label, kind.ToString(), entries);
    }

    private void DrawAlertEntries(
        string label,
        string identity,
        IReadOnlyList<DresserAlertEntry> entries)
    {
        if (entries.Count == 0)
            return;

        if (!ImGui.CollapsingHeader($"{label} ({entries.Count})##{identity}", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        foreach (var category in entries.GroupBy(entry => entry.ItemCategoryId).OrderBy(group => group.Key))
        {
            ImGui.TextDisabled(category.First().ItemCategoryName);
            foreach (var entry in category)
                DrawAlertEntry(entry);
        }
    }

    private void DrawSectionHeading(string label)
    {
        ImGui.Spacing();
        using (sectionFont.Push())
        {
            ImGui.PushStyleColor(ImGuiCol.Text, SpringCleaningUi.Gold);
            ImGui.TextUnformatted(label);
            ImGui.PopStyleColor();
        }
        ImGui.Separator();
    }

    private static void DrawDisabledWrapped(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    private void DrawAlertEntry(DresserAlertEntry entry)
    {
        ImGui.PushID($"alert-{entry.PrismBoxIndex}-{entry.RawItemId}");
        var lookup = new GameIconLookup(entry.IconId, entry.IsHighQuality);
        var texture = textureProvider.GetFromGameIcon(in lookup).GetWrapOrEmpty();
        ImGui.Image(texture.Handle, new Vector2(28));
        ImGui.SameLine();

        var quality = entry.IsHighQuality ? "  HQ" : string.Empty;
        var label = entry.Name + quality;
        if (ImGui.Selectable(label, false, ImGuiSelectableFlags.None, new Vector2(0, 28))
            && controller.HasFreshDuplicateOwnership)
            controller.Restore(entry);

        if (entry.IsAlreadyInArmoire || entry.HasMixedQualitySet)
        {
            ImGui.SameLine();
            if (entry.IsAlreadyInArmoire)
                ImGui.TextDisabled("In Armoire");
            if (entry.HasMixedQualitySet)
            {
                if (entry.IsAlreadyInArmoire)
                    ImGui.SameLine();
                ImGui.TextColored(SpringCleaningUi.Warning, "Mixed HQ/NQ set");
            }
        }

        ImGui.PopID();
    }
}
