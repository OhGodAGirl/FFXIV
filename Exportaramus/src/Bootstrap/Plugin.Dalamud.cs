using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Diagnostics;
using Exportaramus.Features.GuidedExport;
using Exportaramus.Features.Main;
using Exportaramus.Features.Snapshots;
using Exportaramus.Shared.Ui;
using DiagnosticSource = Exportaramus.Features.Diagnostics.DiagnosticSource;
namespace Exportaramus.Bootstrap;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commands;
    private readonly IFramework framework;
    private readonly IPlayerState player;
    private readonly ICondition conditions;
    private readonly IPluginLog log;
    private readonly GameThemeObserver themeObserver;
    private readonly Exportaramus.Features.Inventory.InventoryService inventory;
    private readonly Exportaramus.Features.Inventory.InventoryCaptureAdapter inventoryCapture;
    private readonly Exportaramus.Features.Inventory.InventoryIpcProvider inventoryIpc;
    private readonly Exportaramus.Features.Inventory.RetainerGuideAdapter retainerGuide;
    private readonly Exportaramus.Features.Inventory.RetainerInventoryChangeObserver retainerChanges = new();
    private Task inventoryShutdown = Task.CompletedTask;
    private readonly WindowSystem windows = new(PluginIdentity.Name);
    private readonly DiagnosticMeasurements measurements = new();
    private readonly GuidedExportController guided = new();
    private readonly Queue<MainIntent> intents = new();
    private readonly Queue<DiagnosticsIntent> diagnosticIntents = new();
    private readonly SnapshotController controller;
    private readonly SnapshotCapturePort capturePort;
    private readonly PromptCaptureHints promptHints;
    private ulong hintCharacter;
    private double hintTime;
    private readonly Action<string> acceptHint;
    private readonly GuidedCaptureAdapter guidedAdapter;
    private readonly CollectionDataIpcProvider ipc;
    private readonly CollectionDataIpcProvider ipcV11;
    private readonly CollectionDataIpcProvider ipcV12;
    private readonly MainWindow mainWindow;
    private readonly DiagnosticsWindow diagnosticsWindow;
    private readonly DiagnosticsController diagnostics;
    private readonly IFontHandle titleFont;
    private readonly IFontHandle headingFont;
    private readonly Configuration configuration;
    private readonly ConfigurationPersistence configurationWrites;
    private readonly DiagnosticCollect? contractEvidence;
    private readonly DiagnosticReportSource reportSource;
    private readonly DiagnosticRuntimeMetadata runtimeMetadata;
    private MainViewState view;
    private readonly PresentationInvalidationPolicy presentation = new();
    private bool disposed;
    private bool commandRegistered;
    private Task diagnosticShutdown = Task.CompletedTask;
    private Task configurationShutdown = Task.CompletedTask;
    private bool updateFailed;
    private readonly GuidedCaptureCompletionTracker guidedCompletion = new();

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commands, IDataManager data,
        IUnlockState unlocks, IPlayerState player, IFramework framework, IGameGui gameGui,
        IObjectTable objects, ITargetManager targets, ICondition conditions, IPluginLog log, IGameConfig gameConfig, IAddonLifecycle addonLifecycle, IGameInventory gameInventory)
    {
        this.pluginInterface = pluginInterface; this.commands = commands; this.framework = framework;
        this.player = player; this.conditions = conditions; this.log = log;
        themeObserver = new(gameConfig);
        try
        {
            runtimeMetadata = new(Environment.Version.ToString(), Environment.OSVersion.VersionString,
                ImmutableDictionary<string, string>.Empty
                    .Add("Dalamud", typeof(IDalamudPlugin).Assembly.GetName().Version!.ToString())
                    .Add("Lumina", typeof(Lumina.GameData).Assembly.GetName().Version!.ToString())
                    .Add("LuminaExcel", typeof(Lumina.Excel.Sheets.Item).Assembly.GetName().Version!.ToString())
                    .Add("FFXIVClientStructs", typeof(FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase).Assembly.GetName().Version!.ToString()));
            configuration = pluginInterface.GetPluginConfig() as Configuration ?? new();
            configuration = new() { ReduceMotion = ConfigurationPolicy.RestoreReduceMotion(configuration.Version, configuration.ReduceMotion) };
            configurationWrites = new(pluginInterface.ConfigFile.FullName);
            FFXIVCollectContract.TryLoadEmbedded(out var contract, out _);
            EmbeddedProductData.TryDiagnosticContract(out contractEvidence, out _);
            EmbeddedProductData.TryDerived(out var derived, out _);
            inventory = new(new Exportaramus.Features.Inventory.InventoryFileCache(pluginInterface.GetPluginConfigDirectory()));
            capturePort = new SnapshotCapturePort(data, unlocks, player, framework, gameGui, derived ?? new([], []), measurements, () => inventory.GetSnapshot());
            var storage = new AtomicSnapshotStorage(pluginInterface.GetPluginConfigDirectory(), measurements: measurements);
            controller = new(capturePort, storage, contract, measurements);
            acceptHint = source => controller.HintSource(source, hintCharacter, hintTime);
            promptHints = new(unlocks, gameInventory);

            inventoryCapture = new(inventory, new CollectionReader(data, unlocks, player, framework, gameGui), data, player, framework, gameGui);
            inventoryIpc = new(pluginInterface);
            retainerGuide = new(inventoryCapture, data, player, framework, gameGui, objects, targets, conditions, addonLifecycle, log);
            log.Warning("Exportaramus loaded build {Version}", typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "unknown");
            var validation = GuidedRuntimeValidation.Development(data);
            guidedAdapter = new(player, objects, targets, conditions, gameGui, framework, validation,
                () => data.GameData.Repositories.Values.Single(r => r.ExpansionId == 0).Version, GuidedCaptureAdapter.ReadManagerReady,
                effect => { guidedCompletion.Begin(effect, controller.Publication.Current?.Revision ?? 0); controller.RequestPresence(); },
                effect =>
                {
                    guidedCompletion.Begin(effect, controller.Publication.Current?.Revision ?? 0);
                    if (effect.Kind == GuidedEffectKind.SavePartial) controller.RequestPresence(); else controller.RequestFull();
                });
            reportSource = new(measurements);
            diagnostics = new(reportSource.ReadAsync, measurements, text => ImGui.SetClipboardText(text));
            titleFont = pluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.Jupiter, 24));
            headingFont = pluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.Jupiter, 15));
            view = PrepareView(DateTimeOffset.UtcNow);
            mainWindow = new(() => view, Dispatch, titleFont, headingFont);
            diagnosticsWindow = new(() => diagnostics.View, DispatchDiagnostics, headingFont);
            ipc = new(pluginInterface, controller, measurements);
            ipcV11 = new(pluginInterface, controller, measurements, true);
            ipcV12 = new(pluginInterface, controller, measurements, beasts: true);
            windows.AddWindow(mainWindow); windows.AddWindow(diagnosticsWindow);
            if (!commands.AddHandler("/exportaramus", new CommandInfo(OnCommand) { HelpMessage = "Open Exportaramus.\n/exportaramus diagnostics — Open diagnostics." }))
                throw new InvalidOperationException("The Exportaramus command could not be registered.");
            commandRegistered = true;
            pluginInterface.UiBuilder.Draw += Draw;
            pluginInterface.UiBuilder.OpenMainUi += OpenMain;
            pluginInterface.UiBuilder.OpenConfigUi += OpenMain;
            conditions.ConditionChange += OnConditionChange;
            framework.Update += Update;
        }
        catch { Dispose(); throw; }
    }
    private void OpenMain() => mainWindow.IsOpen = true;
    private void OnCommand(string command, string arguments)
    {
        if (arguments.Trim().Equals("diagnostics", StringComparison.OrdinalIgnoreCase)) diagnosticsWindow.IsOpen = true;
        else mainWindow.IsOpen = true;
    }
    private void OnConditionChange(ConditionFlag flag, bool value) { /* The next bounded framework observation owns state changes. */ }
    private void Draw()
    {
        if (disposed) return;
        var before = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
        try { windows.Draw(); }
        finally { measurements.Record("Draw", Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before); }
    }
    private void Dispatch(MainIntent intent)
    {
        if (disposed) return;
        if (intent == MainIntent.CopyJson)
        {
            if (controller.Publication.Current?.Export.Json is { } json) ImGui.SetClipboardText(json);
            return;
        }
        IntentAdmissionPolicy.TryEnqueue(intents, intent, IntentAdmissionPolicy.MainCapacity);
    }
    private void DispatchDiagnostics(DiagnosticsIntent intent)
    {
        if (intent == DiagnosticsIntent.CopyReport) diagnostics.Dispatch(intent);
        else IntentAdmissionPolicy.TryEnqueue(diagnosticIntents, intent, IntentAdmissionPolicy.DiagnosticsCapacity);
    }
    private void Update(IFramework unused)
    {
        if (disposed) return;
        var before = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
        var now = DateTimeOffset.UtcNow; var seconds = (double)start / Stopwatch.Frequency;
        try
        {
            themeObserver.Update(seconds);
            var contentId = player.IsLoaded ? player.ContentId : 0;
            var zoning = conditions[ConditionFlag.BetweenAreas] || conditions[ConditionFlag.BetweenAreas51];
            controller.Update(contentId, player.IsLoaded ? player.CharacterName : "", !player.IsLoaded && !zoning && !conditions.Any(), zoning, seconds, now);
            if (hintCharacter != contentId) { hintCharacter = contentId; promptHints.Reset(); }
            hintTime = seconds;
            promptHints.Drain(acceptHint);
            inventory.SetCharacter(controller.ActiveContentId);
            inventory.Pump();
            inventoryCapture.Update(capturePort.Catalog);
            retainerGuide.Update();
            var inventoryState = inventory.GetSnapshot();
            if (retainerChanges.Observe(inventoryState)) controller.RequestPresence();
            inventoryIpc.Update(inventoryState);
            var hasIntent = intents.TryDequeue(out var intent);
            if (hasIntent && intent == MainIntent.Retainers)
            {
                if (retainerGuide.Active) retainerGuide.Cancel();
                else if (!guided.OwnsWindowEdges) retainerGuide.Start();
            }
            if (retainerGuide.Active && hasIntent && intent is MainIntent.GuidedCapture or MainIntent.SyncNow) hasIntent = false;
            var decision = MainIntentPolicy.Decide(hasIntent ? intent : null, guided.OwnsWindowEdges);
            switch (decision.Action)
            {
                case MainUiAction.Close: mainWindow.IsOpen = false; break;
                case MainUiAction.Sync: controller.RequestFull(); break;
                case MainUiAction.ToggleReduceMotion:
                    configuration.ReduceMotion = !configuration.ReduceMotion; configurationWrites.Request(configuration.ReduceMotion); break;
            }
            var command = decision.GuidedCommand;
            if (command != GuidedCommand.None || guided.OwnsWindowEdges)
            {
                var guidedFrame = guidedCompletion.Apply(
                    guidedAdapter.Observe(guided.CurrentSource, TimeSpan.FromSeconds(seconds), false),
                    controller.Publication.Current, controller.Error);
                var effect = guided.Update(guidedFrame, command);
                if (effect.Kind != GuidedEffectKind.None) guidedAdapter.Execute(effect, guided, contentId, TimeSpan.FromSeconds(seconds));
                if (!guided.OwnsWindowEdges) guidedCompletion.Reset();
            }
            if (capturePort.ObserveReadySources(contentId, seconds, guided.OwnsWindowEdges)) controller.RequestPresence();
            if (capturePort.ObserveReadyUnlocks(contentId, seconds)) controller.HintSource("achievements", contentId, seconds);
            if (capturePort.ObserveReadyBeasts(contentId, seconds)) controller.HintSource("beasts", contentId, seconds);
            if (diagnosticIntents.TryDequeue(out var diagnosticIntent)) diagnostics.Dispatch(diagnosticIntent);
            configurationWrites.Update();
            diagnostics.Update(); diagnostics.SetReduceMotion(configuration.ReduceMotion);
            var revision = controller.Publication.Current?.Revision ?? 0;
            if (presentation.ShouldRefresh(seconds, revision, hasIntent, guided.State))
            {
                view = PrepareView(now);
                if (PrepareReport(now) is { } report) reportSource.Set(report);
            }
            updateFailed = false;
        }
        catch (Exception)
        {
            if (!updateFailed) log.Warning("Exportaramus encountered a bounded update failure.");
            updateFailed = true;
        }
        finally { measurements.Record("FrameworkUpdate", Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before); }
    }
    private MainViewState PrepareView(DateTimeOffset now)
    {
        var result = MainSnapshotPresenter.Prepare(controller.Publication.Current, controller.ActiveContentId != 0,
            controller.Error, controller.Busy, configuration.ReduceMotion, guided.View, now, true);
        var storageView = Exportaramus.Features.Inventory.InventoryPresentation.Prepare(inventory?.GetSnapshot(), controller.Publication.Current, now);
        if (inventory?.LastCacheFailure is not null)
            storageView = storageView with { Summary = storageView.Summary + " · Inventory cache could not be saved or restored; retry pending." };
        if (storageView.ConfirmedOutfits is { } count)
            result = result with
            {
                Rows = [.. result.Rows.Select(row => row.Key == "outfits" ? row with
            {
                Count = count + "+", Delta = "", Detail = "At least " + count + " outfits confirmed from last-observed storage, including individually captured retainers. Unread or retained storage prevents a current exact total."
            } : row)]
            };
        result = result with
        {
            Rows = [.. result.Rows, new CollectionRowView("retainers", "Retainers", "Storage",
            storageView.RetainerCount, storageView.RetainerCount == "Unknown" ? UiTone.Muted : UiTone.Primary, "", "", UiTone.Muted,
            storageView.Summary + ". Counts include last-observed inventories; run Retainers to refresh.")]
        };
        return result with
        {
            Inventory = storageView,
            RetainersActive = retainerGuide?.Active ?? false,
            RetainersMessage = retainerGuide?.Message ?? ""
        };
    }
    private DiagnosticReport? PrepareReport(DateTimeOffset now)
    {
        if (contractEvidence is null) return null;
        var bundle = controller.Publication.Current;
        var input = MainSnapshotPresenter.DiagnosticInput(bundle, PluginIdentity.Version, runtimeMetadata.DotnetRuntimeVersion,
            controller.Session, controller.Busy, configuration.ReduceMotion, controller.Reports.Count,
            measurements.Counters, contractEvidence, now);
        var registered = !disposed && commandRegistered;
        var lifecycle = input.Lifecycle with
        {
            State = disposed ? "Disposed" : "Running",
            CancellationRequested = controller.CancellationRequested,
            Registrations = new(registered, registered, commandRegistered, registered, registered, registered, registered, registered),
            OwnedTasks = controller.DiagnosticTasks.Add(capturePort.CatalogTask).Add(diagnostics.OwnedTask).Add(new("Configuration", DiagnosticTaskPolicy.State(configurationWrites.WorkerStatus))),
        };
        var runtime = runtimeMetadata.Apply(input.Runtime with
        {
            FfxivClientVersion = DiagnosticNativeSampling.ClientVersion(framework),
        });
        var enriched = DiagnosticRuntimeEnricher.Apply(input, runtime, lifecycle,
            controller.DiagnosticOperation((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency), controller.StorageDiagnostic,
            measurements.LatestMilliseconds, measurements.HasUnrepresentableSamples,
            DiagnosticSamplingPolicy.PrepareUi(mainWindow.IsOpen ? mainWindow.DiagnosticObservation : null));
        return DiagnosticReportPreparer.Prepare(enriched, now);
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        LifecycleCleanup.Run(
            () => promptHints?.Dispose(),
            () => framework.Update -= Update, () => conditions.ConditionChange -= OnConditionChange,
            () => pluginInterface.UiBuilder.Draw -= Draw, () => pluginInterface.UiBuilder.OpenMainUi -= OpenMain,
            () => pluginInterface.UiBuilder.OpenConfigUi -= OpenMain,
            () => { if (commandRegistered) commands.RemoveHandler("/exportaramus"); },
            windows.RemoveAllWindows,
            () => { if (controller is null) capturePort?.Dispose(); else controller.Dispose(); },
            () => ipc?.Dispose(),
            () => ipcV11?.Dispose(),
            () => ipcV12?.Dispose(),
            () => inventoryIpc?.Dispose(),
            () => retainerGuide?.Dispose(),
            () => { if (inventory is not null) inventoryShutdown = inventory.DisposeAsync().AsTask(); },
            () => { if (diagnostics is not null) diagnosticShutdown = diagnostics.DisposeAsync().AsTask(); },
            () => { if (configurationWrites is not null) configurationShutdown = configurationWrites.DisposeAsync().AsTask(); },
            () => titleFont?.Dispose(), () => headingFont?.Dispose(), intents.Clear, diagnosticIntents.Clear);
    }
}
