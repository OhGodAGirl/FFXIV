using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using SpringCleaning.Shared.CollectionData;

namespace SpringCleaning;

internal sealed class DresserArmoireOverlayWindow : Window, IDisposable
{
    private readonly MigrationController controller;
    private readonly DresserScanner scanner;
    private readonly NativeUi nativeUi;
    private readonly ITextureProvider textureProvider;
    private readonly Configuration configuration;
    private readonly Action configurationChanged;
    private readonly Action<TimeSpan> requestPreviewRefresh;
    private readonly IPluginLog log;
    private IReadOnlyList<EligibleDresserItem> candidates = [];
    private Vector2? lastWindowPosition;
    private bool placementApplied;
    private bool wasDragging;
    private bool stylesPushed;
    private bool presentationReady;
    private bool? moveButtonEnabled;
    private bool RetainList => controller.RetainPresentationDuringRefresh && presentationReady;

    public DresserArmoireOverlayWindow(
                MigrationController controller,
                DresserScanner scanner,
                NativeUi nativeUi,
                ITextureProvider textureProvider,
                Configuration configuration,
                Action configurationChanged,
                Action<TimeSpan> requestPreviewRefresh,
                IPluginLog log)
                : base(
                    "Dresser to Armoire##SpringCleaningDresserArmoireOverlay",
                    ImGuiWindowFlags.NoSavedSettings
                    | ImGuiWindowFlags.NoDecoration
                    | ImGuiWindowFlags.NoResize
                    | ImGuiWindowFlags.NoScrollbar
                    | ImGuiWindowFlags.NoScrollWithMouse
                    | ImGuiWindowFlags.NoNav)
    {
        this.controller = controller;
        this.scanner = scanner;
        this.nativeUi = nativeUi;
        this.textureProvider = textureProvider;
        this.configuration = configuration;
        this.configurationChanged = configurationChanged;
        this.requestPreviewRefresh = requestPreviewRefresh;
        this.log = log;
    }

    public bool Requested { get; private set; }

    public void Dispose()
    {
    }

    public void ToggleRequested()
    {
        Requested = !Requested;
        requestPreviewRefresh(TimeSpan.Zero);
        if (Requested)
            placementApplied = false;
    }

    public void UpdateCandidates(IReadOnlyList<EligibleDresserItem> updatedCandidates)
    {
        ArgumentNullException.ThrowIfNull(updatedCandidates);
        candidates = updatedCandidates;
        moveButtonEnabled = null;
        presentationReady = false;
    }

    public void Reset()
    {
        SaveRememberedPosition();
        Requested = false;
        IsOpen = false;
        placementApplied = false;
        wasDragging = false;
    }

    public override unsafe void PreDraw()
    {
        try
        {
            var address = nativeUi.GetDresserAddon();
            if (address == 0)
            {
                IsOpen = false;
                return;
            }

            var dresser = (AtkUnitBase*)address;
            var scale = MathF.Max(0.5f, dresser->Scale);
            var dresserWidth = dresser->GetScaledWidth(true);
            var dresserHeight = dresser->GetScaledHeight(true);
            var horizontalInset = 18f * scale;
            var position = new Vector2(
                dresser->X + horizontalInset,
                dresser->Y + (132f * scale));
            var size = new Vector2(
                dresserWidth - (horizontalInset * 2f),
                dresserHeight - (132f * scale));
            if (!placementApplied)
            {
                var initialPosition = DresserArmoireOverlayPolicy.GetInitialPosition(
                    position,
                    configuration.RememberWindowPlacement,
                    configuration.DresserArmoireOverlayX,
                    configuration.DresserArmoireOverlayY);
                ImGui.SetNextWindowPos(initialPosition, ImGuiCond.Always);
                placementApplied = true;
            }
            ImGui.SetNextWindowSize(
                new Vector2(MathF.Max(360f, size.X), MathF.Max(220f, size.Y)),
                ImGuiCond.Always);

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 8f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 3f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 2f);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 3f);
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(5f, 3f));
            ImGui.PushStyleVar(
                ImGuiStyleVar.Alpha,
                Math.Clamp(ImGui.GetStyle().Alpha * (dresser->Alpha / 255f), 0f, 1f));
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.205f, 0.195f, 0.17f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.16f, 0.155f, 0.14f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.50f, 0.43f, 0.31f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.91f, 0.89f, 0.82f, 1f));
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, new Vector4(0.68f, 0.66f, 0.59f, 1f));
            ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, new Vector4(0.31f, 0.28f, 0.22f, 1f));
            ImGui.PushStyleColor(ImGuiCol.TableRowBg, new Vector4(0.20f, 0.19f, 0.17f, 1f));
            ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, new Vector4(0.235f, 0.22f, 0.195f, 1f));
            ImGui.PushStyleColor(ImGuiCol.TableBorderLight, new Vector4(0.40f, 0.35f, 0.27f, 0.8f));
            stylesPushed = true;
        }
        catch (Exception ex)
        {
            log.Debug(ex, "Could not place the Dresser-to-Armoire view inside the Glamour Dresser.");
            IsOpen = false;
        }
    }

    public override void PostDraw()
    {
        if (!stylesPushed)
            return;

        ImGui.PopStyleColor(9);
        ImGui.PopStyleVar(6);
        stylesPushed = false;
    }

    public UiDrawObservation DrawObservation { get; private set; }

    public override void Draw()
    {
        DrawObservation = UiDrawObservation.Capture();
        if (!controller.RetainPresentationDuringRefresh)
            presentationReady = controller.HasFreshDresserArmoireOwnership;
        DrawMovableHeader();
        ImGui.TextDisabled("Move these Glamour Dresser entries into the Armoire.");
        ImGui.Spacing();

        var footerHeight = controller.Phase == MigrationPhase.Idle ? 47f : 72f;
        ImGui.BeginChild(
            "##EmbeddedDresserArmoireList",
            new Vector2(0f, MathF.Max(100f, ImGui.GetContentRegionAvail().Y - footerHeight)),
            true);
        DrawCandidateList();
        ImGui.EndChild();
        ImGui.Spacing();

        if (controller.IsActive && controller.Mode == MigrationMode.DresserToArmoire)
        {
            ImGui.TextWrapped(controller.Status);
            if (SpringCleaningUi.DrawNativeButton(
                    "CancelEmbeddedDresserArmoire",
                    "Cancel",
                    new Vector2(ImGui.GetContentRegionAvail().X, 32f),
                    false))
            {
                controller.Cancel();
            }
            return;
        }

        if (controller.Phase != MigrationPhase.Idle)
        {
            ImGui.TextDisabled(controller.Status);
            ImGui.SameLine();
        }

        var availableWidth = ImGui.GetContentRegionAvail().X;
        var canMove = controller.CanStart && candidates.Count != 0;
        if (!RetainList || moveButtonEnabled is null)
            moveButtonEnabled = canMove;
        const string moveLabel = "Move Eligible Items to Armoire";
        var requestedButtonWidth = MathF.Max(260f, ImGui.CalcTextSize(moveLabel).X + 28f);
        var buttonWidth = MathF.Min(requestedButtonWidth, availableWidth);
        ImGui.SetCursorPosX(
            ImGui.GetCursorPosX() + MathF.Max(0f, availableWidth - buttonWidth));
        if (SpringCleaningUi.DrawNativeButton(
                "MoveEmbeddedDresserArmoire",
                moveLabel,
                new Vector2(buttonWidth, 32f),
                !moveButtonEnabled.Value) && canMove)
        {
            controller.Start();
        }
    }

    private void DrawMovableHeader()
    {
        var countText = $"{candidates.Count:N0} eligible entr{(candidates.Count == 1 ? "y" : "ies")}"
            + (RetainList ? " - Refreshing, kupo!" : string.Empty);
        var headerHeight = ImGui.GetFrameHeight() + 5f;
        ImGui.InvisibleButton(
            "##DresserArmoireOverlayDragHandle",
            new Vector2(ImGui.GetContentRegionAvail().X, headerHeight));
        var minimum = ImGui.GetItemRectMin();
        var maximum = ImGui.GetItemRectMax();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(
            minimum,
            maximum,
            ImGui.GetColorU32(new Vector4(0.27f, 0.25f, 0.205f, 1f)),
            2f);
        drawList.AddLine(
            new Vector2(minimum.X, maximum.Y),
            maximum,
            ImGui.GetColorU32(new Vector4(0.52f, 0.44f, 0.30f, 0.95f)),
            1f);
        var titlePosition = minimum + new Vector2(7f, 3f);
        drawList.AddText(
            titlePosition,
            ImGui.GetColorU32(SpringCleaningUi.Gold),
            "DRESSER TO ARMOIRE");
        var countSize = ImGui.CalcTextSize(countText);
        drawList.AddText(
            new Vector2(maximum.X - countSize.X - 7f, titlePosition.Y),
            ImGui.GetColorU32(new Vector4(0.70f, 0.68f, 0.61f, 1f)),
            countText);

        var dragging = ImGui.IsItemActive()
            && ImGui.IsMouseDragging(ImGuiMouseButton.Left);
        if (dragging)
        {
            var movedPosition = ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta;
            ImGui.SetWindowPos(movedPosition, ImGuiCond.Always);
            lastWindowPosition = movedPosition;
            wasDragging = true;
        }
        else
        {
            lastWindowPosition = ImGui.GetWindowPos();
            if (wasDragging && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                SaveRememberedPosition();
                wasDragging = false;
            }
        }
    }

    private void SaveRememberedPosition()
    {
        if (!configuration.RememberWindowPlacement
            || lastWindowPosition is not { } position)
        {
            return;
        }

        if (configuration.DresserArmoireOverlayX == position.X
            && configuration.DresserArmoireOverlayY == position.Y)
        {
            return;
        }

        configuration.DresserArmoireOverlayX = position.X;
        configuration.DresserArmoireOverlayY = position.Y;
        configurationChanged();
    }

    private void DrawCandidateList()
    {
        if (!controller.HasFreshDresserArmoireOwnership && !RetainList)
        {
            var waiting = controller.OwnershipReadiness(CollectionSourceName.GlamourDresser, CollectionSourceName.Armoire);
            var waitingSize = ImGui.CalcTextSize(waiting);
            var waitingAvailable = ImGui.GetContentRegionAvail();
            ImGui.SetCursorPos(new Vector2(
                MathF.Max(8f, (waitingAvailable.X - waitingSize.X) * 0.5f),
                MathF.Max(8f, (waitingAvailable.Y - waitingSize.Y) * 0.5f)));
            ImGui.TextDisabled(waiting);
            return;
        }

        if (candidates.Count == 0)
        {
            var message = scanner.IsDresserLoaded
                ? "Nothing in the Glamour Dresser currently needs moving."
                : "Waiting for the Glamour Dresser to finish loading.";
            var size = ImGui.CalcTextSize(message);
            var available = ImGui.GetContentRegionAvail();
            ImGui.SetCursorPos(new Vector2(
                MathF.Max(8f, (available.X - size.X) * 0.5f),
                MathF.Max(8f, (available.Y - size.Y) * 0.5f)));
            ImGui.TextDisabled(message);
            return;
        }

        if (!ImGui.BeginTable(
                "##EmbeddedDresserArmoireCandidates",
                2,
                ImGuiTableFlags.RowBg
                | ImGuiTableFlags.BordersInnerH
                | ImGuiTableFlags.ScrollY
                | ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Dresser Entry", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Contents", ImGuiTableColumnFlags.WidthFixed, 116f);
        ImGui.TableHeadersRow();
        foreach (var item in candidates)
        {
            ImGui.TableNextRow(ImGuiTableRowFlags.None, 38f);
            ImGui.TableSetColumnIndex(0);
            var alert = controller.Alerts.FirstOrDefault(entry => entry.PrismBoxIndex == item.PrismBoxIndex);
            if (alert is not null)
            {
                var lookup = new GameIconLookup(alert.IconId, alert.IsHighQuality);
                var texture = textureProvider.GetFromGameIcon(in lookup).GetWrapOrEmpty();
                ImGui.Image(texture.Handle, new Vector2(30f));
                ImGui.SameLine();
            }
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 6f);
            ImGui.TextUnformatted(item.Name);

            ImGui.TableSetColumnIndex(1);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 6f);
            ImGui.TextDisabled(item.IsOutfit
                ? $"{item.Components.Count:N0} pieces"
                : "Single item");
        }
        ImGui.EndTable();
    }
}
