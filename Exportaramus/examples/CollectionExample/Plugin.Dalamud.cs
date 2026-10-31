using System.Globalization;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Exportaramus.ConsumerKit.V1_2;

namespace CollectionExample;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/collectionexample";
    private readonly IFramework framework;
    private readonly IPlayerState player;
    private readonly ICommandManager commands;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly CollectionDataIpcClient collections;
    private readonly Func<string> inventoryCapabilities;
    private readonly Func<uint, string> inventoryItem;
    private Task refresh = Task.CompletedTask;
    private Task<InventoryItemReader.Result?>? lookup;
    private string? activeCharacter;
    private long characterGeneration;
    private long lookupGeneration;
    private uint? requestedItem;
    private bool commandRegistered;
    private bool disposed;

    public Plugin(IDalamudPluginInterface pluginInterface, IFramework framework, IPlayerState player,
        ICommandManager commands, IChatGui chat, IPluginLog log)
    {
        this.framework = framework;
        this.player = player;
        this.commands = commands;
        this.chat = chat;
        this.log = log;

        // Construct host bindings here; the worker invokes only Exportaramus' managed getters.
        var caps = pluginInterface.GetIpcSubscriber<string>("Exportaramus.Inventory.V1.GetCapabilities");
        var item = pluginInterface.GetIpcSubscriber<uint, string>("Exportaramus.Inventory.V1.GetItem");
        inventoryCapabilities = () => caps.InvokeFunc();
        inventoryItem = id => item.InvokeFunc(id);
        collections = new((changed, withdrawn) =>
            new CollectionDataGateBindings(pluginInterface, changed, withdrawn));
        try
        {
            commandRegistered = commands.AddHandler(Command, new CommandInfo(OnCommand)
            {
                HelpMessage = "Show recorded mounts, or use /collectionexample item <base-item-id>.",
            });
            if (!commandRegistered)
                throw new InvalidOperationException("The example command is already registered.");
            framework.Update += OnUpdate;
        }
        catch { Dispose(); throw; }
    }

    private void UpdateCharacter()
    {
        var id = player.IsLoaded ? player.ContentId : 0;
        var current = id == 0 ? null : id.ToString(CultureInfo.InvariantCulture);
        if (current == activeCharacter) return;
        activeCharacter = current;
        // A queued result belongs to the character that requested it, even if login changes mid-read.
        characterGeneration++;
        requestedItem = null;
    }

    private void OnUpdate(IFramework _)
    {
        if (disposed) return;
        UpdateCharacter();
        if (refresh.IsFaulted)
            log.Error(refresh.Exception!, "The collection refresh failed.");
        refresh = collections.TickAsync(activeCharacter, DateTimeOffset.UtcNow);

        if (lookup is { IsCompleted: true })
        {
            var completed = lookup;
            lookup = null;
            try
            {
                var result = completed.GetAwaiter().GetResult();
                if (lookupGeneration == characterGeneration && activeCharacter is not null)
                {
                    if (result is null) chat.Print("Inventory data is unavailable.");
                    else ShowItem(result);
                }
            }
            catch (Exception error)
            {
                log.Warning(error, "The inventory lookup failed.");
                if (lookupGeneration == characterGeneration && activeCharacter is not null)
                    chat.Print("Inventory data is unavailable or the response was rejected.");
            }
        }

        // Keep at most one lookup in flight; consume its result on the framework thread before starting another.
        if (lookup is null && requestedItem is { } itemId && activeCharacter is { } character)
        {
            requestedItem = null;
            lookupGeneration = characterGeneration;
            lookup = Task.Run(() => InventoryItemReader.Read(
                inventoryCapabilities(), inventoryItem(itemId), itemId, character));
        }
    }

    private void OnCommand(string command, string arguments)
    {
        UpdateCharacter();
        if (activeCharacter is null)
        {
            chat.Print("Log into a character first.");
            return;
        }
        var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 0)
        {
            if (parts.Length != 2 || parts[0] != "item" ||
                !uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var itemId) ||
                itemId is 0 or >= 1_000_000)
            {
                chat.Print("Use /collectionexample or /collectionexample item <base-item-id>.");
                return;
            }
            if (lookup is not null || requestedItem is not null)
            {
                chat.Print("An inventory lookup is already running.");
                return;
            }
            requestedItem = itemId;
            chat.Print("Reading the recorded inventory...");
            return;
        }

        var view = collections.View;
        if (view.Availability != CollectionDataAvailability.Ready ||
            view.Snapshot is not { } snapshot || snapshot.ContentId != activeCharacter)
        {
            chat.Print("Collection data is unavailable or refreshing. Check Exportaramus and try again.");
            return;
        }
        if (!snapshot.SourceHealth.TryGetValue("mounts", out var health) ||
            health.SourceStatus != "Fresh" || health.Completeness != "Complete" ||
            health.ProvenanceType != "Observed" || !snapshot.Unlocks.TryGetValue("mounts", out var mounts))
        {
            chat.Print("A current, complete mount observation is unavailable.");
            return;
        }
        chat.Print($"Exportaramus recorded {mounts.UnlockedIds.Length} unlocked mounts.");
    }

    private void ShowItem(InventoryItemReader.Result result)
    {
        chat.Print($"Item {result.ItemId}: {result.Normal} normal-quality and {result.HighQuality} high-quality, last observed.");
        foreach (var row in result.Locations)
        {
            var amount = row.Normal is null ? "present; copy count unavailable" :
                $"{row.Normal} normal-quality, {row.HighQuality} high-quality";
            chat.Print($"{row.Name} ({row.Kind}): {amount}; {row.State}; observed {row.ObservedAtUtc:u}.");
        }
        if (result.UnknownLocations)
            chat.Print("Some storage has not been read, or the employed-retainer list is unknown.");
        if (result.RetainedLocations)
            chat.Print("Some locations contain retained observations or had a later read failure.");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        framework.Update -= OnUpdate;
        if (commandRegistered) commands.RemoveHandler(Command);
        requestedItem = null;
        characterGeneration++;
        // Stop callbacks first, then settle work owned by this plugin before its assembly unloads.
        // These workers do not wait for framework callbacks; adding such a dependency would make this wait unsafe.
        try { collections.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        catch (Exception error) { log.Error(error, "Collection cleanup failed."); }
        try { lookup?.GetAwaiter().GetResult(); }
        catch (Exception error) { log.Warning(error, "The final inventory lookup did not complete successfully."); }
    }
}
