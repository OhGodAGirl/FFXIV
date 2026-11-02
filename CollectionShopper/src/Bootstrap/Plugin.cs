using Dalamud.Game.Config;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Command;
using Dalamud.Game.Network.Structures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace CollectionShopper;

/// <summary>
/// Owns CollectionShopper's services, windows, commands, and safe teardown.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    private static readonly TimeSpan MarketBoardPollInterval = TimeSpan.FromMilliseconds(500);

    private readonly WindowSystem windowSystem = new(PluginIdentity.InternalName);
    private readonly CancellationTokenSource cancellation = new();
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IClientState clientState;
    private readonly IFramework framework;
    private readonly IPlayerState playerState;
    private readonly IDataManager dataManager;
    private readonly IGameGui gameGui;
    private readonly IMarketBoard marketBoard;
    private readonly IAddonLifecycle addonLifecycle;
    private readonly IPluginLog log;
    private readonly FfxivCollectClient client;
    private readonly ExportaramusConsumer collectionDataClient;
    private readonly ShoppingController shoppingController;
    private readonly MainWindow window;
    private readonly DiagnosticsWindow diagnosticsWindow;
    private readonly MarketBoardBookmarkWindow bookmarkWindow;
    private readonly ThemeObservation themeObservation;
    private DateTime nextMarketBoardCheck;
    private ulong observedContentId;
    private bool wasMarketBoardOpen;
    private bool diagnosticPlayerLoaded;
    private DateTimeOffset? diagnosticObservedAt;
    private bool disposed;
    private PurchaseTrackingState purchaseTracking = PurchaseTrackingPolicy.Prepare([], new HashSet<uint>(), 0, DateTimeOffset.MinValue);
    internal IReadOnlySet<uint> PurchaseSuppressionIds
    {
        get
        {
            if (purchaseTracking.ContentId != CurrentContentId) RefreshPurchaseTracking();
            return purchaseTracking.PendingItemIds;
        }
    }

    private void RefreshPurchaseTracking()
        => purchaseTracking = PurchaseTrackingPolicy.Prepare(Configuration.PurchaseTracking,
            Configuration.CompletedItemIds, CurrentContentId, DateTimeOffset.UtcNow);

    private void ReconcilePurchaseTracking()
    {
        if (!PurchaseTrackingPolicy.Reconcile(purchaseTracking, Configuration.PurchaseTracking,
            CurrentContentId, collectionDataClient.State)) return;
        RefreshPurchaseTracking();
        SaveConfiguration();
        window.NotifyPurchaseTrackingChanged();
    }

    private readonly IGameConfig gameConfig;

    /// <summary>
    /// Creates the plugin and registers its Market Board sidecar lifecycle.
    /// </summary>
    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IClientState clientState,
        IFramework framework,
        IPlayerState playerState,
        IDataManager dataManager,
        IGameGui gameGui,
        IGameConfig gameConfig,
        IMarketBoard marketBoard,
        IAddonLifecycle addonLifecycle,
        ITextureProvider textureProvider,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.clientState = clientState;
        this.framework = framework;
        this.playerState = playerState;
        this.dataManager = dataManager;
        this.gameGui = gameGui;
        this.gameConfig = gameConfig;
        this.marketBoard = marketBoard;
        this.addonLifecycle = addonLifecycle;
        this.log = log;
        client = new FfxivCollectClient();
        collectionDataClient = new ExportaramusConsumer((changed, withdrawn) =>
            new ExportaramusTransport(pluginInterface, changed, withdrawn));
        Configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.CompletedItemIds ??= [];
        Configuration.PurchaseTracking ??= [];
        MigrateConfiguration();
        RefreshPurchaseTracking();
        shoppingController = new ShoppingController(this, framework, marketBoard, gameGui, log);
        window = new MainWindow(
            this,
            pluginInterface,
            client,
            shoppingController,
            framework,
            playerState,
            dataManager,
            textureProvider,
            log,
            cancellation.Token);
        bookmarkWindow = new MarketBoardBookmarkWindow(
            window,
            pluginInterface,
            gameGui,
            log);
        themeObservation = new ThemeObservation(
            () => this.gameConfig.TryGet(SystemConfigOption.ColorThemeType, out uint theme) ? theme : null,
            theme =>
            {
                bookmarkWindow.SetGameTheme(theme);
                PluginTheme.SetGameTheme(theme);
            });
        diagnosticsWindow = new DiagnosticsWindow(CaptureDiagnostics);
        addonLifecycle.RegisterListener(AddonEvent.PostDraw, "ItemSearch", bookmarkWindow.ObserveNativeDraw);
        // Keep the interactive companion in the established WindowSystem order.
        // Reversing these two windows while repairing native z-order caused the
        // shopping surface to render normally but stop receiving button input.
        windowSystem.AddWindow(window);
        windowSystem.AddWindow(bookmarkWindow);
        windowSystem.AddWindow(diagnosticsWindow);

        var commandInfo = new CommandInfo(HandleCommand)
        {
            ShowInHelp = false,
        };
        commandManager.AddHandler(PluginIdentity.CommandName, commandInfo);
        commandManager.AddHandler(PluginIdentity.LegacyCommandName, commandInfo);
        // Dalamud dispatches chat through the root command, but its help renderer
        // lists registered keys. Expose each subcommand once with description only.
        commandManager.AddHandler(PluginIdentity.CommandName + " diagnostics", new CommandInfo((_, _) => diagnosticsWindow.Open())
        {
            HelpMessage = "Open the Collection Shopper Diagnostics window.",
            DisplayOrder = 0,
        });
        commandManager.AddHandler(PluginIdentity.CommandName + " motion", new CommandInfo((_, _) => bookmarkWindow.StartMotionTrace())
        {
            HelpMessage = "Start logging for Collection Shopper motion issues.",
            DisplayOrder = 1,
        });
        pluginInterface.UiBuilder.Draw += DrawWindows;
        pluginInterface.UiBuilder.OpenMainUi += window.Toggle;
        pluginInterface.UiBuilder.OpenConfigUi += window.Toggle;
        clientState.Login += OnLogin;
        clientState.Logout += OnLogout;
        framework.Update += OnFrameworkUpdate;
    }

    internal Configuration Configuration { get; }

    /// <summary>
    /// Stops active work and unregisters every owned callback and window.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        addonLifecycle.UnregisterListener(AddonEvent.PostDraw, "ItemSearch", bookmarkWindow.ObserveNativeDraw);
        cancellation.Cancel();
        clientState.Logout -= OnLogout;
        clientState.Login -= OnLogin;
        framework.Update -= OnFrameworkUpdate;
        pluginInterface.UiBuilder.Draw -= DrawWindows;
        pluginInterface.UiBuilder.OpenMainUi -= window.Toggle;
        pluginInterface.UiBuilder.OpenConfigUi -= window.Toggle;
        commandManager.RemoveHandler(PluginIdentity.CommandName);
        commandManager.RemoveHandler(PluginIdentity.LegacyCommandName);
        commandManager.RemoveHandler(PluginIdentity.CommandName + " diagnostics");
        commandManager.RemoveHandler(PluginIdentity.CommandName + " motion");
        windowSystem.RemoveAllWindows();
        window.Dispose();
        shoppingController.Dispose();
        collectionDataClient.Dispose();
        client.Dispose();
        cancellation.Dispose();
    }

    internal void SaveConfiguration() => pluginInterface.SavePluginConfig(Configuration);

    internal ulong CurrentContentId => playerState.IsLoaded ? playerState.ContentId : 0;
    internal uint CurrentWorldId => playerState.IsLoaded ? playerState.CurrentWorld.RowId : 0;

    internal CollectionDataOwnershipState ReadCollectionDataOwnership()
    {
        var state = collectionDataClient.State;
        _ = PurchaseSuppressionIds;
        return PurchaseTrackingPolicy.ApplyOwnership(purchaseTracking, CurrentContentId, state);
    }

    internal ItemOwnership ReadPurchaseReadiness(ShoppingItem item, ulong runContentId)
    {
        _ = PurchaseSuppressionIds;
        return PurchaseTrackingPolicy.Readiness(purchaseTracking, Configuration.PurchaseTracking,
            item, CurrentContentId, runContentId, collectionDataClient.State);
    }

    internal bool TryTakeCollectionDataRefreshRequest()
        => collectionDataClient.TryTakeRefreshRequest();

    internal void MarkPurchased(uint itemId, ulong purchaseContentId)
    {
        if (!PurchaseTrackingPolicy.RecordConfirmed(Configuration.PurchaseTracking, Configuration.CompletedItemIds,
            itemId, purchaseContentId, DateTimeOffset.UtcNow)) return;
        RefreshPurchaseTracking();
        SaveConfiguration();
        if (purchaseContentId == CurrentContentId)
            _ = collectionDataClient.ReportConfirmedAcquisition(purchaseContentId, itemId);
        window.NotifyPurchased(itemId);
    }

    private void DrawWindows()
    {
        // Capture current native order before either surface draws, while retaining
        // the established companion-before-rail order for ImGui input handling.
        bookmarkWindow.PrepareNativeFrame();
        windowSystem.Draw();
        bookmarkWindow.FocusMarketBoardFromUncoveredClick(
            (window.IsOpen && window.DrawConditions() && window.IsHovered)
            || (bookmarkWindow.IsOpen && bookmarkWindow.DrawConditions() && bookmarkWindow.IsHovered));
    }

    private void HandleCommand(string _, string arguments)
    {
        switch (PluginCommandPolicy.Resolve(arguments))
        {
            case PluginCommandAction.Motion:
                bookmarkWindow.StartMotionTrace();
                break;
            case PluginCommandAction.Diagnostics:
                diagnosticsWindow.Open();
                break;
            default:
                window.Toggle();
                break;
        }
    }

    internal IReadOnlyList<CollectionDataOwnershipSourceState> DiagnosticSources => window.CollectionDataOwnership.Sources;

    private DiagnosticSnapshot CaptureDiagnostics()
    {
        var catalogReady = window.IsCatalogReady;
        var marketBoardOpen = wasMarketBoardOpen;
        var compendiumSnapshotReady = window.CollectionDataOwnership.WasChecked
            && window.CollectionDataOwnership.Snapshot is not null;
        var inputs = new DiagnosticInputs(
            Configuration.Version,
            Configuration.CurrentVersion,
            diagnosticPlayerLoaded,
            catalogReady,
            window.IsCollectionLoaded,
            window.IsRefreshingCollection,
            marketBoardOpen,
            shoppingController.IsRunning,
            compendiumSnapshotReady,
            window.CollectionDataOwnership);

        return new DiagnosticSnapshot(
            PluginIdentity.PublicName,
            PluginIdentity.InternalName,
            typeof(Plugin).Assembly.GetName().Version?.ToString(4) ?? "Unknown",
            PluginIdentity.DalamudApiLevel,
            GetInstallChannel(),
            DateTimeOffset.UtcNow,
            DiagnosticPolicy.BuildChecks(inputs).Concat([
                bookmarkWindow.CaptureTabInputDiagnostics(false),
                bookmarkWindow.CaptureTabInputDiagnostics(true),
            ]).ToArray(),
            typeof(Plugin).Assembly.ManifestModule.ModuleVersionId,
            shoppingController.CaptureDiagnostics(),
            window.CollectionDataOwnership.Sources.ToArray(),
            diagnosticObservedAt,
            Enum.GetValues<ShoppingCategory>().Select(category => new DiagnosticPredicate(
                category.ToString(),
                compendiumSnapshotReady ? window.CollectionDataOwnership.CompleteFor(category) : null,
                "Complete ownership observations",
                compendiumSnapshotReady ? window.CollectionDataOwnership.CompleteFor(category).ToString() : "Not observed")).ToArray());
    }

    private string GetInstallChannel()
    {
        var type = pluginInterface.GetType();
        if (type.GetProperty("IsDev")?.GetValue(pluginInterface) is true)
            return "Development";
        if (type.GetProperty("IsTesting")?.GetValue(pluginInterface) is true)
            return "Testing";
        return "Stable";
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        HandleCharacterChange();
        collectionDataClient.Tick(CurrentContentId, DateTimeOffset.UtcNow);

        var now = DateTime.UtcNow;
        if (now < nextMarketBoardCheck)
            return;
        nextMarketBoardCheck = now + MarketBoardPollInterval;
        // Read the native theme on the framework thread; retain the palette on failure.
        themeObservation.Update();

        var marketBoardOpen = MarketBoardContext.IsBrowsing(name =>
        {
            var addon = gameGui.GetAddonByName(name);
            return !addon.IsNull && addon.IsVisible;
        });

        if (marketBoardOpen && !wasMarketBoardOpen)
            window.RequestMarketPriceRefresh();
        ReconcilePurchaseTracking();
        window.TryRefreshCollectionDataOwnership();
        window.TryStartRequestedMarketPriceRefresh();
        if (!marketBoardOpen && wasMarketBoardOpen)
            window.IsOpen = false;
        bookmarkWindow.IsOpen = marketBoardOpen;
        wasMarketBoardOpen = marketBoardOpen;
        diagnosticPlayerLoaded = playerState.IsLoaded;
        diagnosticObservedAt = DateTimeOffset.UtcNow;
    }

    private void OnLogin()
    {
        collectionDataClient.ClearCharacter();
        observedContentId = 0;
        window.InvalidateCharacterContext(
            "Character session changed. Refresh collection data for the current character.");
    }

    private void OnLogout(int _, int __)
    {
        collectionDataClient.ClearCharacter();
        observedContentId = 0;
        window.InvalidateCharacterContext(
            "Logged out. Refresh collection data after signing in again.");
    }

    private void HandleCharacterChange()
    {
        var contentId = CurrentContentId;
        if (contentId == observedContentId)
            return;

        collectionDataClient.ClearCharacter();
        observedContentId = contentId;
        window.InvalidateCharacterContext(contentId == 0
            ? "No current character session is loaded. Refresh after signing in again."
            : "Current character changed. Refresh collection data for the active character.");
    }

    private void MigrateConfiguration()
    {
        var migration = ConfigurationMigrationPolicy.Evaluate(Configuration.Version, Configuration.ActionDelayMilliseconds);
        if (!migration.Changed) return;
        Configuration.ActionDelayMilliseconds = migration.ActionDelayMilliseconds;
        Configuration.Version = migration.Version;
        pluginInterface.SavePluginConfig(Configuration);
    }


}



