using BetterGlamorPlates.Adapters;
using BetterGlamorPlates.Adapters.CollectionData;
using BetterGlamorPlates.Features.Diagnostics;
using BetterGlamorPlates.Features.Examine;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;
using BetterGlamorPlates.Shared.CollectionData;
using BetterGlamorPlates.Shared.Operations;
using Dalamud.Game.Command;
using Dalamud.Game.Config;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace BetterGlamorPlates.Bootstrap;

/// <summary>
/// Composes the Better Glamour Plates plugin and owns its runtime resources.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    private readonly ThemeObserver themeObserver;
    private readonly IClientState clientState;
    private readonly IPlayerState playerState;
    private readonly NativePlateEditor nativeEditor;
    private readonly PreviewCoordinator preview;
    private readonly CommandDispatcher commands;
    private readonly CommandExecutionCoordinator commandExecution;
    private readonly EditorLifecycle editorLifecycle;
    private readonly NativeEditorBounds nativeBounds;
    private readonly MainController controller;
    private readonly ExportaramusConsumer collectionDataClient;
    private readonly OwnedOperationRegistry operations;
    private readonly MainWindow mainWindow;
    private readonly NativeExamineReader examineReader;
    private readonly ExamineSaveWindow examineWindow;
    private readonly ExamineSaveCoordinator examineSave;
    private readonly DiagnosticJournal diagnostics = new();
    private readonly DiagnosticJournal examineReadiness = new();
    private readonly DiagnosticsWindow diagnosticsWindow;
    private DiagnosticContext? diagnosticContext;
    private readonly DiagnosticRefreshSchedule diagnosticRefresh = new();
    private readonly WindowSystem windowSystem = new(PluginIdentity.InternalName);
    private readonly OwnedTeardown teardown;
    private readonly GameIconCatalog gameIcons;
    private readonly IconCatalogPublication iconPublication;
    private readonly CollectionCoordinator collections;

    /// <summary>
    /// Initializes the plugin, its windows, command, and framework callbacks.
    /// </summary>
    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IPluginLog log,
        IFramework framework,
        IDataManager dataManager,
        IGameGui gameGui,
        IClientState clientState,
        IPlayerState playerState,
        ITextureProvider textureProvider,
        IObjectTable objectTable,
        IGameConfig gameConfig)
    {
        this.clientState = clientState;
        this.playerState = playerState;
        teardown = new OwnedTeardown(exception =>
            log.Error(exception, "Failed to release an owned Better Glamour Plates resource."));

        try
        {
            var store = new ConfigurationStore(pluginInterface);
            gameIcons = new GameIconCatalog(dataManager);
            teardown.Add(gameIcons.Dispose);
            var nativeCollectionValidator = new NativeCollectionValidator(dataManager);
            nativeEditor = new NativePlateEditor(clientState, dataManager, gameGui, log, diagnostics);
            var dyes = nativeEditor.ReadDyes();
            teardown.Add(nativeEditor.ReleasePreview);
            nativeBounds = new NativeEditorBounds(gameGui);
            operations = new OwnedOperationRegistry(exception =>
                log.Error(exception, "A queued Better Glamour Plates operation failed."));
            teardown.Add(operations.Dispose);
            collectionDataClient = new ExportaramusConsumer(
                (changed, withdrawn) => new CollectionDataGateBindings(pluginInterface, changed, withdrawn),
                exception => log.Error(exception, "Exportaramus consumer cleanup failed."));
            controller = new MainController(
                store,
                nativeCollectionValidator,
                new DelegatingSharedCollectionSnapshotSource(() => collectionDataClient.Latest),
                nativeEditor,
                nativeEditor, diagnostics, dyes, gameIcons.IsValid);
            operations.AddDisposeWhenIdle(controller.Dispose);
            operations.AddDisposeWhenIdle(collectionDataClient.Dispose);

            mainWindow = new MainWindow(
                pluginInterface,
                controller,
                textureProvider,
                QueueCommand);
            teardown.Add(mainWindow.Dispose);
            mainWindow.Dyes = dyes;
            iconPublication = new IconCatalogPublication(() => gameIcons.Complete, gameIcons.Advance,
                gameIcons.Dispose, () => gameIcons.Icons,
                icons => { mainWindow.GameIcons = icons; mainWindow.GameIconsReady = true; },
                exception =>
                {
                    mainWindow.GameIconsUnavailable = true;
                    log.Error(exception, "Game icon catalog could not be read. Direct icon selection remains available.");
                });
            preview = new PreviewCoordinator(
                () => new(mainWindow.IsOpen, nativeEditor.IsOpen, nativeEditor.IsDyeWindowOpen, GetCurrentContentId(), controller.View),
                () => Environment.TickCount64, nativeEditor.PreviewOutfit, nativeEditor.ReleasePreview,
                controller.ReportPreviewResult,
                () => diagnostics.Finish(DiagnosticStop.Rejected, DateTimeOffset.UtcNow),
                exception => log.Error(exception, "The outfit preview stopped. No automatic retry was made after the failure."));
            teardown.Add(preview.Dispose);
            collections = new CollectionCoordinator(operations, GetCurrentContentId, collectionDataClient.TryTakeRefreshRequest,
                (owner, token) => Task.Run(() => collectionDataClient.RefreshAsync(owner, DateTimeOffset.UtcNow), token),
                (action, token) => framework.RunOnTick(action, TimeSpan.Zero, 0, token),
                () => collectionDataClient.Latest, collectionDataClient.ClearCharacter,
                controller.ClearSharedCollectionData, controller.UpdateSharedCollectionData, preview.Cancel,
                message => log.Information(message));
            commandExecution = new CommandExecutionCoordinator(
                () => new(mainWindow.IsOpen, nativeEditor.IsOpen, nativeEditor.IsDyeWindowOpen, GetCurrentContentId(), controller.View),
                command => controller.Execute(command, DateTime.UtcNow, Guid.NewGuid()), preview, diagnostics,
                () => DateTimeOffset.UtcNow, command => controller.ReportOperationFailure(command, DateTimeOffset.UtcNow),
                () => { collectionDataClient.RequestRefresh(); collections.TryQueueRefresh(); });
            commands = new CommandDispatcher(operations,
                (action, token) => framework.RunOnTick(action, TimeSpan.Zero, 0, token),
                GetCurrentContentId, command => commandExecution.Execute(command), queued => mainWindow.LoadQueued = queued,
                controller.ReportLoadCancelled, () => new CancellationTokenSource());
            mainWindow.CancelLoad = commands.CancelLoad;
            operations.AddDisposeWhenIdle(commands.Dispose);
            editorLifecycle = new EditorLifecycle(controller.ResetUnsavedChanges, preview.Cancel,
                ShowWhenAvailable, () => mainWindow.IsOpen = false);
            diagnosticsWindow = new DiagnosticsWindow(() => diagnostics.ToJson(DateTimeOffset.UtcNow, diagnosticContext))
            {
                Size = new System.Numerics.Vector2(720, 540),
                SizeCondition = Dalamud.Bindings.ImGui.ImGuiCond.FirstUseEver,
            };
            mainWindow.OpenDiagnostics = () => diagnosticsWindow.IsOpen = true;
            windowSystem.AddWindow(diagnosticsWindow);

            examineReader = new NativeExamineReader(gameGui, objectTable, diagnostics);
            examineWindow = new ExamineSaveWindow(pluginInterface, SaveExaminedOutfit);
            teardown.Add(examineWindow.Dispose);
            themeObserver = new ThemeObserver(
                () => gameConfig.TryGet(SystemConfigOption.ColorThemeType, out uint theme) ? theme : null,
                theme => examineWindow.Theme = ExamineButtonTheme.Resolve(theme));
            examineSave = new ExamineSaveCoordinator(operations,
                (action, token) => framework.RunOnTick(action, TimeSpan.Zero, 0, token), GetCurrentContentId,
                (entity, name) => examineReader.Capture(entity, name, DateTime.UtcNow, Guid.NewGuid()),
                controller.ImportExaminedOutfit, saving => examineWindow.Saving = saving, status => examineWindow.Status = status,
                diagnostics, () => DateTimeOffset.UtcNow,
                exception => log.Error(exception, "Failed to save examined glamour."));
            windowSystem.AddWindow(examineWindow);
            windowSystem.AddWindow(mainWindow);
            teardown.Add(windowSystem.RemoveAllWindows);

            commandManager.AddHandler(PluginIdentity.CommandName, new CommandInfo(OnCommand)
            {
                ShowInHelp = false,
            });
            teardown.Add(() => commandManager.RemoveHandler(PluginIdentity.CommandName));
            // Chat dispatch uses the root; installer help displays the full subcommand once.
            commandManager.AddHandler(PluginIdentity.CommandName + " diagnostics", new CommandInfo((_, _) => diagnosticsWindow.IsOpen = true)
            {
                HelpMessage = "Open the Diagnostics window for Better Glamour Plates.",
            });
            teardown.Add(() => commandManager.RemoveHandler(PluginIdentity.CommandName + " diagnostics"));

            framework.Update += OnFrameworkUpdate;
            teardown.Add(() => framework.Update -= OnFrameworkUpdate);

            pluginInterface.UiBuilder.Draw += windowSystem.Draw;
            teardown.Add(() => pluginInterface.UiBuilder.Draw -= windowSystem.Draw);
            pluginInterface.UiBuilder.OpenMainUi += ShowWhenAvailable;
            teardown.Add(() => pluginInterface.UiBuilder.OpenMainUi -= ShowWhenAvailable);
            pluginInterface.UiBuilder.OpenConfigUi += ShowWhenAvailable;
            teardown.Add(() => pluginInterface.UiBuilder.OpenConfigUi -= ShowWhenAvailable);
        }
        catch
        {
            teardown.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Cancels owned work and releases registrations and resources in reverse
    /// ownership order.
    /// </summary>
    public void Dispose() => teardown.Dispose();

    private void OnCommand(string command, string arguments)
    {
        if (RuntimeUiPolicy.IsDiagnosticsCommand(arguments))
            diagnosticsWindow.IsOpen = true;
    }

    private void ShowWhenAvailable() => RuntimeUiPolicy.ShowWhenAvailable(nativeEditor.IsOpen,
        () => mainWindow.IsOpen = true, () => QueueCommand(new MainCommand.RefreshCollection()));

    private void OnFrameworkUpdate(IFramework currentFramework)
    {
        themeObserver.Update(Environment.TickCount64);
        iconPublication.Update();
        var examined = examineReader.ReadView();
        if (RuntimeUiPolicy.ExamineChanged(examineWindow.View?.EntityId, examined?.EntityId))
            examineWindow.Status = string.Empty;
        examineWindow.View = examined;
        examineWindow.IsOpen = examined is not null;
        mainWindow.NativeDyeWindowOpen = nativeEditor.IsDyeWindowOpen;
        collections.SynchronizeCharacter();
        var currentCollection = collectionDataClient.Latest;
        collections.ObserveSnapshot();
        mainWindow.NativeBounds = nativeBounds.Read();
        var diagnosticTime = DateTimeOffset.UtcNow;
        if (diagnosticRefresh.Take(diagnosticTime))
        {
            examineReadiness.Begin(DiagnosticPhase.ExamineSave, diagnosticTime);
            examineReader.ObserveDiagnostics(examined, examineReadiness);
            diagnosticContext = new(diagnosticTime, nativeEditor.IsOpen, examined is not null,
                currentCollection is not null, controller.ProviderFailure, currentCollection?.CapturedAtUtc,
                DiagnosticJournal.Source(currentCollection?.GlamourDresser), DiagnosticJournal.Source(currentCollection?.Armoire))
            {
                ExamineChecks = examineReadiness.Current.Checks,
                NativeDyeWindowOpen = mainWindow.NativeDyeWindowOpen,
            };
            diagnostics.Context = diagnosticContext;
        }
        if (!editorLifecycle.Update(mainWindow.IsOpen, nativeEditor.IsOpen, mainWindow.NativeDyeWindowOpen))
            return;

        collections.TryQueueRefresh();
        preview.Advance();
    }

    private void SaveExaminedOutfit(ExamineView view) => examineSave.Save(view.EntityId, view.Name);

    private void QueueCommand(MainCommand command) => commands.Queue(command);

    private ulong GetCurrentContentId()
        => RuntimeUiPolicy.CharacterId(clientState.IsLoggedIn, () => playerState.IsLoaded, () => playerState.ContentId);

}
