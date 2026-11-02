using Dalamud.Game.Config;
using System.Globalization;
using System.Diagnostics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Exportaramus.ConsumerKit.V1_1;
using EZDelivery.Features.Collections;
using EZDelivery.Features.ExpertDelivery;
using EZDelivery.Features.Interface;
using EZDelivery.Features.Shopping;
using UiCategory = EZDelivery.Features.Interface.ShopCategory;
using PurchaseCategory = EZDelivery.Features.Shopping.ShopCategory;

namespace EZDelivery.Bootstrap;

/// <summary>Creates the composition root with injected Dalamud services.</summary>
public sealed class Plugin(IDalamudPluginInterface pluginInterface, IFramework framework, IGameGui gui,
    IClientState client, IPlayerState player, IDataManager data, ITextureProvider textures,
    IGameConfig gameConfig, ICommandManager commands, IPluginLog log, INotificationManager notifications, IAddonLifecycle lifecycle) : IAsyncDalamudPlugin
{
    private readonly InterfaceFacade ui = new();
    private readonly CollectionKnowledge collection = new();
    private InterfaceRenderer? renderer;
    private ExpertDeliveryFacade? delivery;
    private DeliveryNativeAdapter? deliveryNative;
    private ShoppingFacade? shopping;
    private CollectionDataIpcClient? provider;
    private Configuration configuration = new();
    private NativeBuild build = new(false, "Native build not checked");
    private Task providerWork = Task.CompletedTask;
    private bool attached;
    private bool commandRegistered;
    private bool disposed;
    private bool faulted;
    private bool reconciliationFailureLogged;
    private string scope = "";
    private long generation;
    private string failure = "";
    private string reportStatus = "No acquisition reports";
    private readonly ThemeObserver themeObserver = new();
    private long frameworkSamples;
    private double frameworkTotalMs;
    private double frameworkMaxMs;
    private bool deliveryWasRunning;
    private int resetPurchasesRequested;
    private readonly QualificationCapture qualification = new();
    private string? qualificationCommand;
    private readonly List<string> attachedResources = [];
    private static readonly string PluginVersion = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "Unknown";
    private static readonly string LoadedBuildId = typeof(Plugin).Assembly.ManifestModule.ModuleVersionId.ToString("D");

    /// <summary>Initializes services and registers commands and UI events on the framework thread.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            build = await Task.Run(NativeBuild.Inspect, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await framework.RunOnFrameworkThread(() =>
            {
                configuration = pluginInterface.GetPluginConfig() as Configuration ?? new();
                ConfigurationPolicy.ValidateVersion(configuration.Version);
                provider = new((changed, withdrawn) => new CollectionDataGateBindings(pluginInterface, changed, withdrawn));
                attachedResources.Add("Provider");
                deliveryNative = new(gui, client, data, lifecycle, build.Supported);
                attachedResources.Add("DeliveryNative");
                delivery = new(deliveryNative);
                shopping = new(data, gui, client, Knowledge, () => build.Supported);
                shopping.Acquired += Acquired;
                attachedResources.Add("Acquired");
                delivery.Delivered += Delivered;
                attachedResources.Add("Delivered");
                renderer = new(ui);
                attachedResources.Add("Renderer");
                if (!commands.AddHandler("/ezdelivery", new CommandInfo(Command) { HelpMessage = "settings - Open the EZ Delivery settings window.\n/ezdelivery diagnostics - Open the EZ Delivery diagnostics window." }))
                    throw new InvalidOperationException("The /ezdelivery command is already registered.");
                commandRegistered = true;
                attachedResources.Add("Command");
                // Unsubscribing is safe even if a later event attachment fails partway through load.
                attached = true;
                pluginInterface.UiBuilder.Draw += Draw;
                attachedResources.Add("Draw");
                pluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
                attachedResources.Add("OpenConfigUi");
                pluginInterface.UiBuilder.OpenMainUi += OpenSettings;
                attachedResources.Add("OpenMainUi");
                framework.Update += Update;
                attachedResources.Add("Update");
                TraceLifecycle("Loaded", [], []);
            }).ConfigureAwait(false);
        }
        catch (Exception loadError)
        {
            try { await DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanupError)
            {
                throw new AggregateException("EZ Delivery load and cleanup failed.", loadError, cleanupError);
            }
            throw;
        }
    }

    private PurchaseKnowledge Knowledge(ShopOffer offer)
    {
        var kind = offer.Category switch { PurchaseCategory.Barding => CollectionKind.Barding, PurchaseCategory.Furniture => CollectionKind.Furniture, _ => CollectionKind.Equipment };
        var result = collection.Evaluate(offer.ItemId, kind, offer.BardingUnlockId);
        return new(result.State switch { OwnershipState.Present => PurchasePresence.Present, OwnershipState.Missing => PurchasePresence.Missing, _ => PurchasePresence.Unknown }, result.Reason);
    }

    private void Acquired(ShopOffer offer)
    {
        collection.ConfirmAcquisition(offer.ItemId);
        var character = Character();
        reportStatus = character is null || provider is null ? "Unavailable" : provider.ReportConfirmedAcquisition(character, offer.ItemId, "EZDelivery");
    }

    private void Delivered(ulong character, uint item, DateTimeOffset when)
    {
        collection.ConfirmDelivery(character.ToString(CultureInfo.InvariantCulture), item, when);
        shopping?.ConfirmDelivery(character, item);
    }

    private string? Character() => client.IsLoggedIn && player.ContentId != 0 ? player.ContentId.ToString(CultureInfo.InvariantCulture) : null;

    private uint? ReadGameTheme() => gameConfig.TryGet(SystemConfigOption.ColorThemeType, out uint theme) ? theme : null;

    private void Update(IFramework unused)
    {
        ProcessQualificationCommand();
        var measurement = qualification.Begin();
        try { UpdateCore(); }
        finally { qualification.End(false, measurement); }
    }

    private void UpdateCore()
    {
        if (disposed || delivery is null || shopping is null || provider is null || renderer is null) return;
        themeObserver.Update(Environment.TickCount64, ReadGameTheme);
        PluginTheme.SetGameTheme(themeObserver.Current);
        // Commands enqueue work; the framework callback owns its lifetime and error boundary.
        if (Interlocked.Exchange(ref resetPurchasesRequested, 0) != 0 && !delivery.IsRunning && !shopping.IsRunning)
        {
            collection.ResetPurchaseTracking(DateTimeOffset.UtcNow);
            shopping.ResetPurchaseTracking();
            reportStatus = "EZ Delivery purchase tracking reset; Exportaramus storage and unlock checks remain active";
            ui.DiagnosticsOpen = true;
        }
        if (faulted)
        {
            // A presentation/persistence failure must not abandon an action already sent.
            // Stop leaves only receipt reconciliation active in either engine.
            ReconcileAfterFailure("Expert Delivery", () =>
            {
                delivery.Stop();
                if (delivery.IsRunning) delivery.Tick(Environment.TickCount64);
            });
            ReconcileAfterFailure("Shopping", () =>
            {
                shopping.Stop();
                if (shopping.IsRunning) shopping.Tick();
            });
            ui.Prepare(ui.State with
            {
                Generation = generation,
                Delivery = new(delivery.IsRunning, false, delivery.Status.Message, delivery.Status.ConfirmedCount),
                Shopping = ui.State.Shopping with { Running = shopping.IsRunning, Rows = [], CollectionUsable = false, Status = shopping.Status },
                AnyOperationRunning = delivery.IsRunning || shopping.IsRunning,
                DiagnosticsReport = failure + "\nDelivery: " + delivery.Status.Message + "\nShopping: " + shopping.Status,
            });
            return;
        }
        var started = Stopwatch.GetTimestamp();
        try
        {
            var intents = ui.DrainIntents();
            // Cancellation must precede any frame that could send the next native action.
            foreach (var intent in intents)
            {
                var route = UiIntentRouting.EvaluateBeforeTick(intent);
                if (route.StopDelivery) delivery.Stop();
                if (route.StopPurchases) shopping.Stop();
            }
            if (providerWork.IsFaulted)
            {
                log.Error(providerWork.Exception!, "Exportaramus refresh failed");
                providerWork = Task.CompletedTask;
            }
            var character = Character();
            providerWork = provider.TickAsync(character, DateTimeOffset.UtcNow);
            collection.Update(provider.View, character);
            var now = Environment.TickCount64;
            delivery.Tick(now);
            shopping.Tick();
            var observation = delivery.Observation;
            var shopObservation = shopping.Observation;
            var newScope = $"{character}:{observation.Context?.Addon}:{observation.Filter}:{observation.Sort}:{shopObservation.Identity}:{collection.Generation}";
            if (newScope != scope) { scope = newScope; generation++; }
            foreach (var intent in intents)
            {
                // Stop is always honored; starts and presentation changes are bound to the prepared context.
                var route = UiIntentRouting.EvaluateAfterTick(intent, generation);
                if (route.StopDelivery) delivery.Stop();
                if (route.StopPurchases) shopping.Stop();
                if (!route.ProcessCurrent) continue;
                if (ui.HandlePresentationIntent(intent)) continue;
                switch (intent)
                {
                    case StartDeliveryIntent when !delivery.IsRunning && !shopping.IsRunning:
                        delivery.Toggle(now); break;
                    case BuyOneIntent one when !delivery.IsRunning && !shopping.IsRunning:
                        if (ui.Browser.Rows.Any(row => row.OfferKey == one.OfferKey)) shopping.BuyOne(one.OfferKey);
                        break;
                    case BuyAllIntent all when !delivery.IsRunning && !shopping.IsRunning:
                        if (all.OfferKeys.SequenceEqual(ui.Browser.BuyAllKeys)) shopping.BuyAll(all.OfferKeys);
                        break;
                    case ReduceMotionIntent or ShopTabStyleIntent:
                        ConfigurationPolicy.TryApply(configuration, intent, generation,
                            () => pluginInterface.SavePluginConfig(configuration), out var saveError);
                        if (saveError is not null)
                        {
                            failure = "Settings could not be saved; the current setting remains active.";
                            log.Warning(saveError, "EZ Delivery settings save failed");
                        }
                        break;
                    case CopyDiagnosticsIntent copy:
                        // Clipboard writes belong to the UI draw thread; handled in Draw from this prepared request.
                        clipboard = copy.PreparedReport;
                        break;
                }
            }
            if (deliveryWasRunning && !delivery.IsRunning)
                notifications.AddNotification(new Notification
                {
                    Title = "EZ Delivery",
                    Content = delivery.Status.Message,
                    Type = delivery.Status.Reason is DeliveryReason.Empty or DeliveryReason.Cancelled or DeliveryReason.SealCapacity ? NotificationType.Info : NotificationType.Warning
                });
            deliveryWasRunning = delivery.IsRunning;
            string? unknown = null;
            var preparedRows = new List<ShopRow>();
            foreach (var offer in shopping.Offers)
            {
                if (offer.Company != shopObservation.Identity.Company || offer.RequiredRank > shopObservation.Identity.Rank) continue;
                var knowledge = Knowledge(offer);
                if (unknown is null && knowledge.Presence == PurchasePresence.Unknown &&
                    (ui.SelectedCategory is null || (UiCategory)offer.Category == ui.SelectedCategory))
                    unknown = knowledge.Reason;
                if (knowledge.Presence != PurchasePresence.Missing || shopping.Suppressed.Contains(offer.ItemId)) continue;
                preparedRows.Add(new(offer.OfferKey, offer.ItemId, offer.IconId, offer.Name, (UiCategory)offer.Category, offer.UnitCost,
                    build.Supported && shopObservation.Ready, build.Supported ? shopObservation.Reason : "Native build is not supported"));
            }
            var rows = preparedRows.ToArray();
            var anchor = observation.Anchor;
            var shopAnchor = shopping.Anchor;
            var diagnostics = ui.State.DiagnosticsReport;
            if (ui.DiagnosticsOpen)
                diagnostics = new DiagnosticReport("1.0", DateTimeOffset.UtcNow, PluginVersion, build.Supported, build.Detail,
                delivery.Status.Phase.ToString(), delivery.Status.Reason.ToString(), delivery.Status.ConfirmedCount,
                shopping.Status, shopping.ConfirmedCount, collection.Status, unknown, reportStatus,
                failure.Length == 0 ? [] : [failure], frameworkSamples, frameworkSamples == 0 ? 0 : frameworkTotalMs / frameworkSamples, frameworkMaxMs)
                {
                    DeliveryDialogCheck = deliveryNative?.LastDialogCheck ?? "Not observed",
                    DeliveryDialogCheckedAtUtc = deliveryNative?.LastDialogCheckAtUtc,
                    ShoppingDialogCheck = shopping.LastDialogCheck,
                    DeliveryMappingCheck = deliveryNative?.LastMappingCheck ?? "Not observed",
                    DeliveryRefreshCheck = deliveryNative?.RefreshCheck ?? "Not observed",
                    LastNativeDeliveryRefreshAtUtc = deliveryNative?.LastNativeRefreshAtUtc,
                    LoadedBuildId = LoadedBuildId
                }.ToJson();
            ui.Prepare(new(generation, character is not null,
                anchor is null ? null : new(new(anchor.X, anchor.Y, anchor.Width, anchor.Height), true),
                shopAnchor is null ? null : new(new(shopAnchor.X, shopAnchor.Y, shopAnchor.Width, shopAnchor.Height), true),
                anchor is not null, new(delivery.IsRunning, build.Supported && observation.State is DeliveryReadState.Ready or DeliveryReadState.ReadyEmpty, delivery.Status.Message, delivery.Status.ConfirmedCount),
                new(shopping.IsRunning, collection.Available && (unknown is null || rows.Any(row => ui.SelectedCategory is null || row.Category == ui.SelectedCategory)), unknown ?? collection.Status, rows, shopObservation.Seals, shopping.Status, shopping.ConfirmedCount),
                delivery.IsRunning || shopping.IsRunning, configuration.ReduceMotion, diagnostics)
            { ShopTabStyle = TabPresentation.Normalize(configuration.ShopTabStyle) });
            renderer.PrepareTextures(textures, rows);
        }
        catch (Exception error)
        {
            delivery.Stop(); shopping.Stop();
            faulted = true;
            failure = "Operation stopped: " + error.GetType().Name;
            log.Error(error, "EZ Delivery framework operation failed");
            ui.Prepare(ui.State with
            {
                Generation = ++generation,
                DiagnosticsReport = failure,
                Delivery = ui.State.Delivery with { CanStart = false },
                Shopping = ui.State.Shopping with { Rows = [], CollectionUsable = false }
            });
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            frameworkSamples++;
            frameworkTotalMs += elapsed;
            frameworkMaxMs = Math.Max(frameworkMaxMs, elapsed);
        }
    }

    private void ReconcileAfterFailure(string operation, Action reconcile)
    {
        try { reconcile(); }
        catch (Exception error)
        {
            // Both operations keep receiving cancellation/reconciliation ticks independently.
            // A broken native read must neither escape Update nor flood the log every frame.
            if (reconciliationFailureLogged) return;
            reconciliationFailureLogged = true;
            failure += $"\n{operation} receipt reconciliation failed: {error.GetType().Name}";
            log.Error(error, "EZ Delivery receipt reconciliation failed after an earlier operation failure");
        }
    }

    private string? clipboard;
    private void Draw()
    {
        if (disposed) return;
        var measurement = qualification.Begin();
        try
        {
            if (clipboard is { } text) { clipboard = null; ImGui.SetClipboardText(text); }
            renderer?.Draw();
        }
        catch (Exception error)
        {
            if (!faulted) log.Error(error, "EZ Delivery interface failed; pending actions will be reconciled");
            faulted = true;
            failure = "Interface failed: " + error.GetType().Name;
        }
        finally { qualification.End(true, measurement); }
    }
    private void Command(string command, string args)
    {
        if (args.Trim().StartsWith("qualification ", StringComparison.OrdinalIgnoreCase))
        {
            if (!disposed) Interlocked.CompareExchange(ref qualificationCommand, args.Trim()[14..].Trim(), null);
        }
        else if (args.Trim().Equals("resetpurchases", StringComparison.OrdinalIgnoreCase))
        {
            if (!disposed) Interlocked.Exchange(ref resetPurchasesRequested, 1);
        }
        else if (args.Trim().Equals("diagnostics", StringComparison.OrdinalIgnoreCase)) ui.DiagnosticsOpen = true;
        else OpenSettings();
    }
    private void OpenSettings() => ui.SettingsOpen = true;

    private void ProcessQualificationCommand()
    {
        var request = Interlocked.Exchange(ref qualificationCommand, null);
        if (request is null || disposed) return;
        try
        {
            string message;
            if (request.StartsWith("start ", StringComparison.OrdinalIgnoreCase))
                message = qualification.Start(request[6..].Trim())
                    ? "Qualification capture started: 512 warmup callbacks, then 10,000 Update and Draw samples. Export after completion."
                    : "Capture not started. Use a 1-64 character name containing letters, digits, hyphens or underscores; export the previous capture first.";
            else if (request.Equals("stop", StringComparison.OrdinalIgnoreCase))
            {
                qualification.Stop();
                message = "Qualification capture stopped. Export retains incomplete counts.";
            }
            else if (request.Equals("export", StringComparison.OrdinalIgnoreCase))
                message = "Qualification evidence: " + qualification.Export(Path.Combine(pluginInterface.GetPluginConfigDirectory(), "qualification"), PluginVersion, LoadedBuildId);
            else message = "Use /ezdelivery qualification start <scenario>, stop, or export.";
            log.Information("EZ Delivery {Message}", message);
            notifications.AddNotification(new Notification { Title = "EZ Delivery qualification", Content = message, Type = NotificationType.Info });
        }
        catch (Exception error)
        {
            // Measurement failure must not change either transaction engine.
            log.Warning(error, "EZ Delivery qualification command failed");
            notifications.AddNotification(new Notification { Title = "EZ Delivery qualification", Content = error.Message, Type = NotificationType.Warning });
        }
    }

    private void TraceLifecycle(string phase, IReadOnlyList<string> attempted, IReadOnlyList<string> completed)
    {
        try
        {
            log.Information("EZ Delivery lifecycle {Trace}", System.Text.Json.JsonSerializer.Serialize(new
            {
                Phase = phase,
                Version = PluginVersion,
                LoadedBuildId,
                InstanceId = qualification.InstanceId,
                AtUtc = DateTimeOffset.UtcNow,
                AttachedResources = attachedResources,
                CleanupAttempted = attempted,
                CleanupCompleted = completed,
                Scope = "Top-level owned resources only; completion is return without exception, not an internal retention audit."
            }));
        }
        catch { /* Logging failure must not interrupt cleanup or initialization. */ }
    }

    /// <summary>Stops operations, detaches subscriptions and disposes owned services asynchronously.</summary>
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        qualification.Stop();
        var cleanupAttempted = new List<string>();
        var cleanupCompleted = new List<string>();
        void Release(string name, Action action)
        {
            cleanupAttempted.Add(name);
            action();
            cleanupCompleted.Add(name);
        }
        try
        {
            await framework.RunOnFrameworkThread(() =>
            {
                var detachEvents = attached;
                var removeCommand = commandRegistered;
                attached = false;
                commandRegistered = false;
                CleanupActions.Run(
                    () => { if (detachEvents) Release("Update", () => framework.Update -= Update); },
                    () => { if (detachEvents) Release("Draw", () => pluginInterface.UiBuilder.Draw -= Draw); },
                    () => { if (detachEvents) Release("OpenConfigUi", () => pluginInterface.UiBuilder.OpenConfigUi -= OpenSettings); },
                    () => { if (detachEvents) Release("OpenMainUi", () => pluginInterface.UiBuilder.OpenMainUi -= OpenSettings); },
                    () => { if (removeCommand) Release("Command", () => commands.RemoveHandler("/ezdelivery")); },
                    () => delivery?.Stop(),
                    () => shopping?.Stop(),
                    () => { if (shopping is not null) Release("Acquired", () => shopping.Acquired -= Acquired); },
                    () => { if (delivery is not null) Release("Delivered", () => delivery.Delivered -= Delivered); },
                    () => { if (renderer is not null) Release("Renderer", renderer.Dispose); },
                    () => { if (deliveryNative is not null) Release("DeliveryNative", deliveryNative.Dispose); });
            }).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (provider is not null)
                {
                    cleanupAttempted.Add("Provider");
                    await provider.DisposeAsync().ConfigureAwait(false);
                    cleanupCompleted.Add("Provider");
                }
            }
            finally { TraceLifecycle("Disposed", cleanupAttempted, cleanupCompleted); }
        }
    }
}
