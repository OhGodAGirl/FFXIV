using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SpringCleaning.Shared.CollectionData;

namespace SpringCleaning;

/// <summary>
/// Composes Spring Cleaning and owns its runtime registrations and resources.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    private static readonly TimeSpan UiPreviewRefreshInterval = TimeSpan.FromMilliseconds(500);
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IFramework framework;
    private readonly IGameConfig gameConfig;
    private readonly IPlayerState playerState;
    private readonly IPluginLog log;
    private readonly Configuration configuration;
    private readonly DresserScanner scanner;
    private readonly NativeUi nativeUi;
    private readonly NativeWindowFocusTracker nativeWindowFocusTracker;
    private readonly CollectionDataClient collectionDataClient;
    private readonly CollectionDataRefreshController collectionData;
    private readonly MigrationController controller;
    private readonly MigrationWindow window;
    private readonly SettingsWindow settingsWindow;
    private readonly DresserArmoireOverlayWindow dresserArmoireOverlayWindow;
    private readonly DresserBookmarkWindow dresserBookmarkWindow;
    private readonly QuickStoreWindow quickStoreWindow;
    private readonly DiagnosticWindow diagnosticWindow;
    private readonly OutfitAutofillService outfitAutofill;
    private readonly WindowSystem windowSystem = new("SpringCleaning");
    private readonly RuntimeRegistrationSet registrations = new();
    private readonly object collectionDataRefreshGate = new();
    private readonly CancellationTokenSource collectionDataRefreshCancellation = new();
    private DateTime nextUiPreviewAt;
    private bool configurationSaveRequested;
    private bool disposed;
    private bool windowManuallyOpened;
    private bool wereDresserCompanionsVisible;
    private bool wasArmoireOpen;
    private bool lastRememberWindowPlacement;
    private Task? collectionDataRefreshTask;
    private Task? collectionDataDisposalTask;
    private CollectionDataRefreshCompletion? pendingCollectionDataRefresh;
    private readonly UiFlickerTrace uiFlickerTrace = new();
    private readonly PresentationRefreshPolicy presentationRefresh = new();
    private long uiTraceUpdates;
    private long uiTraceRefreshes;
    private int uiTraceNativeVisible;
    private int uiTraceHideEvents;
    private int uiTraceShowEvents;

    /// <summary>
    /// Initializes the plugin services, windows, automation controller, and callbacks.
    /// </summary>
    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IFramework framework,
        IGameConfig gameConfig,
        IDataManager dataManager,
        IGameGui gameGui,
        IObjectTable objectTable,
        ITargetManager targetManager,
        IClientState clientState,
        IPlayerState playerState,
        ICondition condition,
        IPluginLog log,
        ITextureProvider textureProvider,
        IAddonLifecycle addonLifecycle)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.framework = framework;
        this.gameConfig = gameConfig;
        this.playerState = playerState;
        this.log = log;
        configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (configuration.DiagnosticMode)
        {
            configuration.DiagnosticMode = false;
            pluginInterface.SavePluginConfig(configuration);
        }
        nativeUi = new NativeUi(gameGui, log,
            dataManager.GetExcelSheet<Lumina.Excel.Sheets.Addon>().GetRowOrDefault(15663)?.Text.ExtractText() ?? string.Empty);
        nativeWindowFocusTracker = new NativeWindowFocusTracker(addonLifecycle);
        scanner = new DresserScanner(dataManager);
        collectionDataClient = new CollectionDataClient(new DalamudCollectionDataTransport(pluginInterface));
        collectionData = new CollectionDataRefreshController();
        var navmesh = new VNavmeshIpc(pluginInterface);
        controller = new MigrationController(
            configuration,
            scanner,
            collectionData,
            nativeUi,
            navmesh,
            objectTable,
            targetManager,
            clientState,
            dataManager,
            log);
        var duplicateDiscardController = new DuplicateDiscardController(
            new DuplicateDiscardRuntime(scanner, nativeUi, condition, configuration, collectionData),
            () => DateTime.UtcNow,
            RequestPreviewRefresh);
        duplicateDiscardController.Stopped = (_, count) => nativeUi.Diagnostics.RetainOperationFailure("DuplicateDiscard", count, collectionData.Snapshot);
        var cleanupController = new DresserCleanupController(
            new DresserCleanupRuntime(scanner, nativeUi, collectionData, configuration),
            duplicateDiscardController, () => DateTime.UtcNow);
        cleanupController.Stopped = (_, count) => nativeUi.Diagnostics.RetainOperationFailure("DresserRestoration", count, collectionData.Snapshot);
        window = new MigrationWindow(
            controller,
            scanner,
            duplicateDiscardController,
            cleanupController,
            configuration,
            textureProvider,
            pluginInterface,
            RequestConfigurationSave,
            RequestPreviewRefresh);
        settingsWindow = new SettingsWindow(configuration, RequestConfigurationSave);
        diagnosticWindow = new DiagnosticWindow(
            controller,
            scanner,
            nativeUi,
            settingsWindow,
            collectionData,
            configuration,
            RequestConfigurationSave,
            log);
        dresserArmoireOverlayWindow = new DresserArmoireOverlayWindow(
            controller,
            scanner,
            nativeUi,
            textureProvider,
            configuration,
            RequestConfigurationSave,
            RequestPreviewRefresh,
            log);
        dresserBookmarkWindow = new DresserBookmarkWindow(
            controller,
            window,
            settingsWindow,
            diagnosticWindow,
            dresserArmoireOverlayWindow,
            configuration,
            nativeUi,
            nativeWindowFocusTracker,
            pluginInterface,
            log);
        quickStoreWindow = new QuickStoreWindow(
            configuration,
            controller,
            settingsWindow,
            nativeUi,
            nativeWindowFocusTracker,
            pluginInterface,
            log);
        outfitAutofill = new OutfitAutofillService(addonLifecycle, collectionData, controller, log);
        lastRememberWindowPlacement = configuration.RememberWindowPlacement;
        windowSystem.AddWindow(dresserBookmarkWindow);
        windowSystem.AddWindow(dresserArmoireOverlayWindow);
        windowSystem.AddWindow(window);
        windowSystem.AddWindow(quickStoreWindow);
        windowSystem.AddWindow(settingsWindow);
        windowSystem.AddWindow(diagnosticWindow);

        try
        {
            registrations.Add(
                () => commandManager.AddHandler(PluginIdentity.CommandName, new CommandInfo(OnCommand)
                {
                    HelpMessage = "Use /springcleaning diagnostics for debugging.",
                    ShowInHelp = false,
                }),
                () => commandManager.RemoveHandler(PluginIdentity.CommandName));
            registrations.Add(
                () => framework.Update += OnFrameworkUpdate,
                () => framework.Update -= OnFrameworkUpdate);
            registrations.Add(
                () => pluginInterface.UiBuilder.Draw += DrawWindows,
                () => pluginInterface.UiBuilder.Draw -= DrawWindows);
            registrations.Add(
                () => pluginInterface.UiBuilder.HideUi += ObserveUiHidden,
                () => pluginInterface.UiBuilder.HideUi -= ObserveUiHidden);
            registrations.Add(
                () => pluginInterface.UiBuilder.ShowUi += ObserveUiShown,
                () => pluginInterface.UiBuilder.ShowUi -= ObserveUiShown);
            registrations.Add(
                () => pluginInterface.UiBuilder.OpenMainUi += ToggleWindow,
                () => pluginInterface.UiBuilder.OpenMainUi -= ToggleWindow);
            registrations.Add(
                () => pluginInterface.UiBuilder.OpenConfigUi += ToggleWindow,
                () => pluginInterface.UiBuilder.OpenConfigUi -= ToggleWindow);
        }
        catch
        {
            ReleaseRegistrations();
            DisposeOwnedResources();
            throw;
        }
        log.Warning("Loaded Spring Cleaning dev build {BuildIdentity}.", DevBuildIdentity);
    }

    internal static string DevBuildIdentity =>
        typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "unknown";

    /// <summary>
    /// Cancels active work and releases callbacks and owned resources.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        controller.Cancel();
        ReleaseRegistrations();
        DisposeOwnedResources();
    }

    private void OnCommand(string command, string args)
    {
        if (PluginCommandRouter.Route(args) == PluginCommandDestination.Diagnostics)
        {
            configuration.DiagnosticMode = true;
            RequestConfigurationSave();
            diagnosticWindow.IsOpen = true;
            return;
        }

        // Feature windows are opened from the station tabs, never by a bare command.
    }

    private void ToggleWindow()
    {
        if (!nativeUi.ShouldShowDresserCompanions && !nativeUi.IsArmoireStoreOpen)
        {
            window.IsOpen = false;
            windowManuallyOpened = false;
            return;
        }
        window.OpenPage(SpringCleaningPage.Duplicates);
        windowManuallyOpened = true;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (disposed)
            return;

        quickStoreWindow.UpdateTheme(gameConfig);
        uiTraceUpdates++;

        controller.ObserveOpenStations();
        PumpCollectionData();
        controller.RetainPresentationDuringRefresh = presentationRefresh.RetainDisplay(
            DateTime.UtcNow, GetActiveContentId(), collectionDataClient.PresentationEpoch,
            collectionData.LatestSnapshot is not null, collectionData.Snapshot.RefreshInProgress,
            // An overlapping changed notice supersedes a read without breaking its display scope.
            // Actual withdrawal, handshake failure and character changes still advance the epoch.
            collectionData.Snapshot.LastFailure is not CollectionDataFailure.None and not CollectionDataFailure.StaleCompletion);
        if (presentationRefresh.ClearDisplay)
        {
            window.ClearPresentation();
            window.UpdateDuplicateCandidates([]);
            window.UpdateInventoryDresserCandidates([]);
            window.UpdateInventoryArmoireCandidates([]);
            window.UpdateDresserArmoireCandidates([]);
            dresserArmoireOverlayWindow.UpdateCandidates([]);
            dresserBookmarkWindow.ClearPresentation();
            quickStoreWindow.ClearPresentation();
            controller.ClearPreviewPresentation();
            nextUiPreviewAt = DateTime.MinValue;
        }
        controller.Update(framework);
        window.AdvanceDiscardWorkflow();
        controller.RefreshPreview();
        RefreshUiPreviews();
        if (configuration.DiagnosticMode)
            diagnosticWindow.Observe();

        var dresserOpen = nativeUi.IsDresserOpen;
        var dresserCompanionsVisible = nativeUi.ShouldShowDresserCompanions;
        var armoireOpen = nativeUi.IsArmoireStoreOpen;
        uiTraceNativeVisible = (dresserOpen ? 1 : 0)
            | (dresserCompanionsVisible ? 2 : 0) | (armoireOpen ? 4 : 0);
        var placementPreferenceChanged = lastRememberWindowPlacement
            != configuration.RememberWindowPlacement;
        var snapToDefault = WindowPlacementPolicy.ShouldSnapAtSessionStart(
            configuration.RememberWindowPlacement);
        if (dresserCompanionsVisible
            && (!wereDresserCompanionsVisible || placementPreferenceChanged && snapToDefault))
        {
            dresserBookmarkWindow.BeginDresserSession();
            diagnosticWindow.BeginStationSession();
            window.BeginDresserSession(snapToDefault);
            settingsWindow.BeginStationSession(snapToDefault);
        }
        if (armoireOpen
            && (!wasArmoireOpen || placementPreferenceChanged && snapToDefault))
        {
            diagnosticWindow.BeginStationSession();
            settingsWindow.BeginStationSession(snapToDefault);
        }
        _ = window.ConsumeCloseRequest();

        // Storage tabs execute directly; only explicit Duplicates/Settings navigation opens the drawer.
        if (controller.IsActive)
            window.IsOpen = false;

        var showingDuplicates = window.IsOpen
            && window.ActivePage is SpringCleaningPage.Duplicates or SpringCleaningPage.Settings;
        if (!showingDuplicates && !windowManuallyOpened)
            window.IsOpen = false;

        if (!window.IsOpen)
            windowManuallyOpened = false;
        if (!dresserCompanionsVisible && !armoireOpen && !controller.IsActive)
        {
            if (!window.IsCleanupRunning)
            {
                window.IsOpen = false;
                windowManuallyOpened = false;
            }
            window.ReleaseDresserDock();
            settingsWindow.IsOpen = false;
            dresserArmoireOverlayWindow.Reset();
        }

        dresserBookmarkWindow.IsOpen = dresserCompanionsVisible;
        if (!dresserCompanionsVisible && nativeUi.IsExistingOutfitMergePromptOpen)
            window.SetNativeOcclusion(true);
        dresserArmoireOverlayWindow.IsOpen = false;
        quickStoreWindow.IsOpen = armoireOpen
            && !window.IsCleanupRunning
            && (!controller.IsActive || controller.Mode == MigrationMode.InventoryToArmoire);
        wereDresserCompanionsVisible = dresserCompanionsVisible;
        wasArmoireOpen = armoireOpen;
        lastRememberWindowPlacement = configuration.RememberWindowPlacement;
        PersistConfigurationIfRequested();
    }

    private void ObserveUiHidden() => uiTraceHideEvents++;

    private void ObserveUiShown() => uiTraceShowEvents++;

    private int GetOpenWindowMask() =>
        (dresserBookmarkWindow.IsOpen ? 1 : 0)
        | (dresserArmoireOverlayWindow.IsOpen ? 2 : 0)
        | (window.IsOpen ? 4 : 0)
        | (settingsWindow.IsOpen ? 8 : 0)
        | (quickStoreWindow.IsOpen ? 16 : 0)
        | (diagnosticWindow.IsOpen ? 32 : 0);

    private void DrawWindows()
    {
        if (uiFlickerTrace.Finished)
        {
            windowSystem.Draw();
            return;
        }

        var openBefore = GetOpenWindowMask();
        var entryAlpha = ImGui.GetStyle().Alpha;
        windowSystem.Draw();
        var frame = ImGui.GetFrameCount();
        ReadOnlySpan<UiDrawObservation> observations =
        [
            dresserBookmarkWindow.DrawObservation,
            dresserArmoireOverlayWindow.DrawObservation,
            window.DrawObservation,
            settingsWindow.DrawObservation,
            quickStoreWindow.DrawObservation,
            diagnosticWindow.DrawObservation,
        ];
        var drawn = 0;
        var dim = 0;
        var zero = 0;
        for (var index = 0; index < observations.Length; index++)
        {
            var observation = observations[index];
            if (observation.Frame != frame)
                continue;

            drawn |= 1 << index;
            if (observation.Alpha < 0.99f)
                dim |= 1 << index;
            if (observation.Alpha <= 0f)
                zero |= 1 << index;
        }

        var report = uiFlickerTrace.Observe(DateTime.UtcNow, frame,
            new UiFlickerState(openBefore, GetOpenWindowMask(), drawn, dim, zero,
                entryAlpha, ImGui.GetStyle().Alpha, uiTraceNativeVisible,
                collectionData.LatestSnapshot is not null, uiTraceHideEvents, uiTraceShowEvents),
            uiTraceUpdates, uiTraceRefreshes);
        if (report is not null)
            log.Warning("{UiFlickerReport}", report);
    }

    private void RefreshUiPreviews()
    {
        if (controller.RetainPresentationDuringRefresh)
            return;

        var now = DateTime.UtcNow;
        var previewVisible = window.IsOpen || dresserArmoireOverlayWindow.IsOpen;
        if (!PreviewRefreshPolicy.ShouldRefresh(
                now,
                nextUiPreviewAt,
                previewVisible,
                controller.IsActive))
        {
            return;
        }

        nextUiPreviewAt = PreviewRefreshPolicy.GetNextRefresh(now, UiPreviewRefreshInterval);
        if (window.IsOpen && window.ActivePage == SpringCleaningPage.Duplicates)
            window.UpdateDuplicateCandidates(
                scanner.ScanStoredInventoryDuplicates(collectionData.LatestSnapshot, configuration.ProtectItemsWithMateria, configuration.ProtectItemsWithSpiritbond));

        if (window.IsOpen
            && window.ActivePage == SpringCleaningPage.InventoryToDresser
            && scanner.AreDresserInventoryCandidatesReady)
        {
            window.UpdateInventoryDresserCandidates(
                scanner.ScanEligibleInventoryDresserOutfits(
                    collectionData.LatestSnapshot,
                    configuration.OnlyMoveCompleteOutfits));
        }

        if (window.IsOpen && window.ActivePage == SpringCleaningPage.InventoryToArmoire)
        {
            window.UpdateInventoryArmoireCandidates(
                scanner.ScanEligibleInventoryItems(collectionData.LatestSnapshot));
        }

        if (dresserArmoireOverlayWindow.IsOpen
            || window.IsOpen && window.ActivePage == SpringCleaningPage.DresserToArmoire)
        {
            var candidates = scanner.PlanEligibleDresserItems(collectionData.LatestSnapshot);
            window.UpdateDresserArmoireCandidates(candidates);
            dresserArmoireOverlayWindow.UpdateCandidates(candidates);
        }
    }

    private void RequestConfigurationSave()
    {
        configurationSaveRequested = true;
    }

    private void PersistConfigurationIfRequested()
    {
        if (!configurationSaveRequested)
            return;

        configurationSaveRequested = false;
        pluginInterface.SavePluginConfig(configuration);
    }

    private void RequestPreviewRefresh(TimeSpan delay)
    {
        var requestedAt = PreviewRefreshPolicy.GetNextRefresh(DateTime.UtcNow, delay);
        nextUiPreviewAt = delay == TimeSpan.Zero || requestedAt > nextUiPreviewAt
            ? requestedAt
            : nextUiPreviewAt;
    }

    private void PumpCollectionData()
    {
        CompleteCollectionDataRefreshIfReady();
        var observation = collectionData.Observe(
            GetActiveContentId(),
            collectionDataClient.TryTakeRefreshRequest(DateTimeOffset.UtcNow),
            invalidated: collectionDataClient.Latest is null);
        if (observation.ClearTransport)
            collectionDataClient.ClearCharacter();
        if (observation.RefreshRequest is { } refreshRequest)
            QueueCollectionRefresh(refreshRequest);
    }

    private ulong GetActiveContentId() =>
        playerState.IsLoaded ? playerState.ContentId : 0UL;

    private void QueueCollectionRefresh(CollectionDataRefreshRequest request)
    {
        if (disposed
            || collectionDataRefreshCancellation.IsCancellationRequested
            || collectionDataRefreshTask is { IsCompleted: false })
        {
            return;
        }

        collectionDataRefreshTask = Task.Run(
            () => RunCollectionRefresh(request, collectionDataRefreshCancellation.Token),
            collectionDataRefreshCancellation.Token);
    }

    private void RunCollectionRefresh(
        CollectionDataRefreshRequest request,
        CancellationToken cancellationToken)
    {
        CollectionDataRefreshCompletion completion;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = collectionDataClient.Refresh(request.ContentId, DateTimeOffset.UtcNow);
            cancellationToken.ThrowIfCancellationRequested();
            completion = new CollectionDataRefreshCompletion(
                request.ContentId,
                result,
                collectionDataClient.Latest,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            completion = new CollectionDataRefreshCompletion(
                request.ContentId,
                CollectionDataParseResult.Reject(CollectionDataFailure.ProviderUnavailable),
                collectionDataClient.Latest,
                ex);
        }

        lock (collectionDataRefreshGate)
        {
            if (!disposed && !cancellationToken.IsCancellationRequested)
                pendingCollectionDataRefresh = completion;
        }
    }

    private void CompleteCollectionDataRefreshIfReady()
    {
        var task = collectionDataRefreshTask;
        if (task is null || !task.IsCompleted)
            return;

        CollectionDataRefreshCompletion? completion;
        lock (collectionDataRefreshGate)
        {
            if (!ReferenceEquals(collectionDataRefreshTask, task))
                return;

            collectionDataRefreshTask = null;
            completion = pendingCollectionDataRefresh;
            pendingCollectionDataRefresh = null;
        }

        if (completion is null)
            return;

        uiTraceRefreshes++;

        if (completion.Error is { } error)
            log.Error(error, "Shared collection refresh failed.");

        var followUp = collectionData.Complete(
            completion.ContentId,
            completion.ParseResult,
            completion.LatestSnapshot);
        if (followUp is { } request)
            QueueCollectionRefresh(request);
    }

    private void DisposeCollectionDataRefresh()
    {
        collectionDataRefreshCancellation.Cancel();
        Task? task;
        lock (collectionDataRefreshGate)
        {
            pendingCollectionDataRefresh = null;
            task = collectionDataRefreshTask;
        }

        if (task is null || task.IsCompleted)
        {
            DisposeCollectionDataTransport();
            return;
        }

        collectionDataDisposalTask = DisposeCollectionDataTransportAfterAsync(task);
    }

    private async Task DisposeCollectionDataTransportAfterAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (collectionDataRefreshCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            log.Error(ex, "Shared collection refresh failed while the plugin was unloading.");
        }
        finally
        {
            DisposeCollectionDataTransport();
        }
    }

    private void DisposeCollectionDataTransport()
    {
        collectionDataClient.Dispose();
        collectionDataRefreshCancellation.Dispose();
    }

    private void ReleaseRegistrations()
    {
        foreach (var error in registrations.Dispose())
            log.Error(error, "Failed to release a Spring Cleaning runtime registration.");
    }

    private void DisposeOwnedResources()
    {
        DisposeOwnedResource(collectionData.Dispose, nameof(collectionData));
        DisposeOwnedResource(DisposeCollectionDataRefresh, nameof(collectionDataRefreshTask));
        DisposeOwnedResource(outfitAutofill.Dispose, nameof(outfitAutofill));
        DisposeOwnedResource(windowSystem.RemoveAllWindows, nameof(windowSystem));
        DisposeOwnedResource(diagnosticWindow.Dispose, nameof(diagnosticWindow));
        DisposeOwnedResource(quickStoreWindow.Dispose, nameof(quickStoreWindow));
        DisposeOwnedResource(dresserBookmarkWindow.Dispose, nameof(dresserBookmarkWindow));
        DisposeOwnedResource(nativeWindowFocusTracker.Dispose, nameof(nativeWindowFocusTracker));
        DisposeOwnedResource(dresserArmoireOverlayWindow.Dispose, nameof(dresserArmoireOverlayWindow));
        DisposeOwnedResource(settingsWindow.Dispose, nameof(settingsWindow));
        DisposeOwnedResource(window.Dispose, nameof(window));
    }

    private void DisposeOwnedResource(Action dispose, string resourceName)
    {
        try
        {
            dispose();
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed to dispose Spring Cleaning resource {ResourceName}.", resourceName);
        }
    }
}
