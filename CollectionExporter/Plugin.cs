using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Dalamud.Game.Inventory;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.Exd;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace CollectionExporter;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/collectionexporter";
    private const uint FieldNotesItemActionId = 19743;
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IUnlockState UnlockState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameInventory GameInventory { get; private set; } = null!;

    private readonly Configuration configuration;
    private readonly HttpClient httpClient;
    private readonly CancellationTokenSource disposeTokenSource = new();
    private OnlineCollectionData? onlineCollections;
    private string onlineOmissionReason = "Configure a public FFXIV Collect character profile to include these collections.";

    internal readonly WindowSystem WindowSystem = new("CollectionExporter");
    internal ExportWindow ExportWindow { get; }

    public Plugin()
    {
        configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        httpClient = new HttpClient
        {
            BaseAddress = new Uri($"https://{FfxivCollectProfile.Host}/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CollectionExporter/0.4.1");
        ExportWindow = new ExportWindow(this);
        WindowSystem.AddWindow(ExportWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open CollectionExporter."
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUi;
    }

    public void Dispose()
    {
        disposeTokenSource.Cancel();
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUi;
        CommandManager.RemoveHandler(CommandName);
        WindowSystem.RemoveAllWindows();
        ExportWindow.Dispose();
        httpClient.Dispose();
        disposeTokenSource.Dispose();
    }

    private void OnCommand(string command, string args) => ToggleMainUi();
    public void ToggleMainUi() => ExportWindow.Toggle();

    internal string ConfiguredProfile => configuration.FfxivCollectCharacterId is ulong id
        ? FfxivCollectProfile.GetCharacterUrl(id)
        : string.Empty;

    internal bool TrySetProfile(string input, out string message)
    {
        if (!FfxivCollectProfile.TryParseCharacterId(input, out var id))
        {
            message = "Enter a numeric character ID or a URL like https://ffxivcollect.com/characters/12345678.";
            return false;
        }

        configuration.FfxivCollectCharacterId = id;
        PluginInterface.SavePluginConfig(configuration);
        onlineCollections = null;
        onlineOmissionReason = "FFXIV Collect profile has not been refreshed yet.";
        message = "Profile saved. Generate will verify it against the logged-in character.";
        return true;
    }

    internal async Task RefreshOnlineCollectionsAsync()
    {
        if (configuration.FfxivCollectCharacterId is not ulong characterId)
        {
            onlineCollections = null;
            onlineOmissionReason = "No public FFXIV Collect character profile is configured.";
            return;
        }

        try
        {
            var cancellationToken = disposeTokenSource.Token;
            using var profileDocument = await GetJsonAsync($"api/characters/{characterId}", cancellationToken);
            var profileName = profileDocument.RootElement.GetProperty("name").GetString();
            if (!string.Equals(profileName, PlayerState.CharacterName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"FFXIV Collect profile is {profileName}, but the logged-in character is {PlayerState.CharacterName}.");
            }

            var ownedOutfitsTask = GetOwnedIdsAsync($"api/characters/{characterId}/outfits/owned", cancellationToken);
            var ownedRelicsTask = GetOwnedIdsAsync($"api/characters/{characterId}/relics/owned", cancellationToken);
            var outfitCatalogTask = GetOutfitCatalogAsync(cancellationToken);
            var relicCatalogTask = GetRelicCatalogAsync(cancellationToken);
            await Task.WhenAll(ownedOutfitsTask, ownedRelicsTask, outfitCatalogTask, relicCatalogTask);

            var itemIds = CollectVisibleItemIds();
            var outfitIds = CollectionPayload.MergeOwned(
                await ownedOutfitsTask,
                onlineCollections?.OutfitIds);
            var relicIds = CollectionPayload.MergeOwned(
                await ownedRelicsTask,
                onlineCollections?.RelicIds);

            foreach (var outfit in await outfitCatalogTask)
            {
                if (outfit.ItemIds.Length > 0 && outfit.ItemIds.All(itemIds.Contains))
                    outfitIds.Add(outfit.Id);
            }

            foreach (var relic in await relicCatalogTask)
            {
                if (itemIds.Contains(relic.Id))
                {
                    relicIds.Add(relic.Id);
                    continue;
                }

                if (relic.AchievementId is uint achievementId
                    && UnlockState.IsAchievementListLoaded
                    && DataManager.GetExcelSheet<Lumina.Excel.Sheets.Achievement>().TryGetRow(achievementId, out var achievement)
                    && UnlockState.IsAchievementComplete(achievement))
                {
                    relicIds.Add(relic.Id);
                }
            }

            onlineCollections = new OnlineCollectionData(
                outfitIds.OrderBy(id => id).ToArray(),
                relicIds.OrderBy(id => id).ToArray());
            onlineOmissionReason = string.Empty;
        }
        catch (OperationCanceledException) when (disposeTokenSource.IsCancellationRequested)
        {
            onlineCollections = null;
            onlineOmissionReason = "Profile refresh was cancelled because the plugin was unloaded.";
        }
        catch (Exception ex)
        {
            onlineCollections = null;
            onlineOmissionReason = $"Profile refresh failed: {ex.Message}";
            Log.Warning(ex, "Could not refresh FFXIV Collect profile; outfits and relics will be omitted.");
        }
    }

    internal ExportResult BuildExport()
    {
        if (!PlayerState.IsLoaded)
            throw new InvalidOperationException("Log into a character before exporting.");

        // FFXIV Collect only modifies keys that are present in the payload.
        // We intentionally omit categories whose site IDs cannot be mapped safely
        // from a single public Dalamud unlock-state row.
        var data = new SortedDictionary<string, uint[]>();
        var omitted = new SortedDictionary<string, string>
        {
        };

        if (onlineCollections is null)
        {
            omitted["outfits"] = onlineOmissionReason;
            omitted["relics"] = onlineOmissionReason;
        }
        else
        {
            CollectionPayload.Add(data, "outfits", onlineCollections.OutfitIds);
            CollectionPayload.Add(data, "relics", onlineCollections.RelicIds);
        }

        CollectionPayload.Add(data, "bardings", Collect<BuddyEquip>(UnlockState.IsBuddyEquipUnlocked));
        CollectionPayload.Add(data, "cards", Collect<TripleTriadCard>(UnlockState.IsTripleTriadCardUnlocked));
        CollectionPayload.Add(data, "fashions", Collect<Ornament>(UnlockState.IsOrnamentUnlocked));
        CollectionPayload.Add(data, "frames", Collect<BannerFrame>(UnlockState.IsBannerFrameUnlocked));
        CollectionPayload.Add(data, "occult_records", Collect<MKDLore>(UnlockState.IsMKDLoreUnlocked));
        CollectionPayload.Add(data, "orchestrions", Collect<Orchestrion>(UnlockState.IsOrchestrionUnlocked));
        CollectionPayload.Add(data, "spells", Collect<AozAction>(UnlockState.IsAozActionUnlocked));

        // Leves are also directly represented by a game row and are supported by
        // the FFXIV Collect importer. This records completed leve IDs.
        CollectionPayload.Add(data, "leves", Collect<Leve>(UnlockState.IsLeveCompleted));

        // FFXIV Collect uses CharaMakeCustomize.UnlockLink as its hairstyle ID.
        // Multiple race/gender rows can share one link, so emit the link once if
        // any matching customization row is unlocked.
        CollectionPayload.Add(data, "hairstyles", CollectHairstyles());

        AddFieldRecords(data, omitted);

        // These three collections require explicit game-to-site translations.
        // Only add them after their backing character state is confirmed loaded.
        AddClientStateCollections(data, omitted);

        var json = CollectionPayload.Serialize(data);

        return new ExportResult(data, omitted, json);
    }

    private static uint[] Collect<T>(Func<T, bool> isUnlocked)
        where T : struct, IExcelRow<T>
    {
        var result = new List<uint>();
        var sheet = DataManager.GetExcelSheet<T>();

        foreach (var row in sheet)
        {
            try
            {
                if (row.RowId != 0 && isUnlocked(row))
                    result.Add(row.RowId);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"Failed unlock check for {typeof(T).Name} row {row.RowId}");
            }
        }

        return result.ToArray();
    }

    private static uint[] CollectHairstyles()
    {
        return DataManager.GetExcelSheet<CharaMakeCustomize>()
            .Where(row => row.RowId != 0 && row.UnlockLink != 0)
            .GroupBy(row => (uint)row.UnlockLink)
            .Where(group => group.Any(IsCharaMakeCustomizeUnlockedSafe))
            .Select(group => group.Key)
            .OrderBy(id => id)
            .ToArray();
    }

    private static bool IsCharaMakeCustomizeUnlockedSafe(CharaMakeCustomize row)
    {
        try
        {
            return UnlockState.IsCharaMakeCustomizeUnlocked(row);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, $"Failed hairstyle unlock check for CharaMakeCustomize row {row.RowId}");
            return false;
        }
    }

    private static unsafe void AddFieldRecords(
        SortedDictionary<string, uint[]> data,
        SortedDictionary<string, string> omitted)
    {
        // FFXIV Collect keys Field Records by MYCWarResultNotebook.Number (1-50).
        // The corresponding consumable ItemAction stores that number in Data[0].
        // Validate the complete mapping against the notebook sheet before reading
        // native item-action state so a sheet change can never produce a partial
        // replacement collection.
        var siteIds = DataManager.GetExcelSheet<MYCWarResultNotebook>()
            .Where(row => row.Number != 0 && !row.Name.IsEmpty)
            .Select(row => (uint)row.Number)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        var noteItems = DataManager.GetExcelSheet<Item>()
            .Where(row => row.RowId != 0
                && row.ItemAction.IsValid
                && row.ItemAction.Value.Action.RowId == FieldNotesItemActionId)
            .Select(row => (Item: row, SiteId: (uint)row.ItemAction.Value.Data[0]))
            .Where(entry => entry.SiteId != 0)
            .ToArray();

        var mappedIds = noteItems
            .Select(entry => entry.SiteId)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        if (siteIds.Length == 0 || !siteIds.SequenceEqual(mappedIds))
        {
            Log.Warning(
                "Field Record mapping validation failed: notebook IDs={NotebookCount}, item-action IDs={ItemCount}; omitting field_records.",
                siteIds.Length,
                mappedIds.Length);
            omitted["field_records"] = "The current game-data mapping could not be fully validated.";
            return;
        }

        var unlockedIds = new List<uint>();
        foreach (var entry in noteItems)
        {
            var nativeItem = ExdModule.GetItemRowById(entry.Item.RowId);
            if (nativeItem == null)
            {
                Log.Warning("Native item row {ItemId} was unavailable; omitting field_records.", entry.Item.RowId);
                omitted["field_records"] = "Field Record state was not fully available; try generating again.";
                return;
            }

            var state = UIState.Instance()->IsItemActionUnlocked(nativeItem);
            if (state == 1)
            {
                unlockedIds.Add(entry.SiteId);
            }
            else if (state != 2)
            {
                Log.Warning(
                    "Unexpected Field Record unlock state {State} for item {ItemId}; omitting field_records.",
                    state,
                    entry.Item.RowId);
                omitted["field_records"] = "Field Record ownership was not fully loaded; try generating again.";
                return;
            }
        }

        CollectionPayload.Add(data, "field_records", unlockedIds);
    }

    private static unsafe void AddClientStateCollections(
        SortedDictionary<string, uint[]> data,
        SortedDictionary<string, string> omitted)
    {
        var uiState = UIState.Instance();
        if (uiState == null)
        {
            Log.Warning("UIState was unavailable; omitting armoires, npcs, and survey_records.");
            omitted["armoires"] = "Game UI state was unavailable.";
            omitted["npcs"] = "Game UI state was unavailable.";
            omitted["survey_records"] = "Game UI state was unavailable.";
            return;
        }

        // FFXIV Collect intentionally numbers Cabinet rows as row ID + 1.
        // Cabinet state is not safe to treat as empty until the server-backed
        // cabinet bitset has loaded for this character.
        if (uiState->Cabinet.IsCabinetLoaded())
        {
            var armoires = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Cabinet>()
                .Where(row => row.Order != 0 && uiState->Cabinet.IsItemInCabinet(row.RowId))
                .Select(row => row.RowId + 1)
                .ToArray();
            CollectionPayload.Add(data, "armoires", armoires);
        }
        else
        {
            Log.Warning("Cabinet state was not loaded; omitting armoires.");
            omitted["armoires"] = "Armoire state has not loaded this session; open the Armoire and generate again.";
        }

        // FFXIV Collect stores the TripleTriad/TripleTriadResident row ID.
        var npcs = DataManager.GetExcelSheet<Lumina.Excel.Sheets.TripleTriad>()
            .Where(row => row.RowId != 0 && uiState->IsTripleTriadNpcBeaten(row.RowId))
            .Select(row => row.RowId)
            .ToArray();
        CollectionPayload.Add(data, "npcs", npcs);

        // FFXIV Collect stores VVDNotebookContents row IDs directly. The
        // corresponding PlayerState bit array is indexed by that same row ID.
        var completedNotes = uiState->PlayerState.CompletedVVDNotebookContentsBitArray;
        var surveyRecords = DataManager.GetExcelSheet<VVDNotebookContents>()
            .Where(row => row.RowId != 0 && completedNotes.Get((int)row.RowId))
            .Select(row => row.RowId)
            .ToArray();
        CollectionPayload.Add(data, "survey_records", surveyRecords);
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task<uint[]> GetOwnedIdsAsync(string path, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(path, cancellationToken);
        return document.RootElement.EnumerateArray()
            .Select(row => row.GetProperty("id").GetUInt32())
            .Distinct()
            .ToArray();
    }

    private async Task<OutfitCatalogEntry[]> GetOutfitCatalogAsync(CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync("api/outfits?limit=10000", cancellationToken);
        return document.RootElement.GetProperty("results").EnumerateArray()
            .Select(row => new OutfitCatalogEntry(
                row.GetProperty("id").GetUInt32(),
                row.GetProperty("items").EnumerateArray()
                    .Select(item => item.GetProperty("id").GetUInt32())
                    .Distinct()
                    .ToArray()))
            .ToArray();
    }

    private async Task<RelicCatalogEntry[]> GetRelicCatalogAsync(CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync("api/relics?limit=10000", cancellationToken);
        return document.RootElement.GetProperty("results").EnumerateArray()
            .Select(row => new RelicCatalogEntry(
                row.GetProperty("id").GetUInt32(),
                row.TryGetProperty("achievement_id", out var achievement)
                    && achievement.ValueKind == JsonValueKind.Number
                    ? achievement.GetUInt32()
                    : null))
            .ToArray();
    }

    private static unsafe HashSet<uint> CollectVisibleItemIds()
    {
        var ids = new HashSet<uint>();
        var containers = new[]
        {
            GameInventoryType.Inventory1, GameInventoryType.Inventory2,
            GameInventoryType.Inventory3, GameInventoryType.Inventory4,
            GameInventoryType.EquippedItems,
            GameInventoryType.ArmoryMainHand, GameInventoryType.ArmoryOffHand,
            GameInventoryType.ArmoryHead, GameInventoryType.ArmoryBody,
            GameInventoryType.ArmoryHands, GameInventoryType.ArmoryWaist,
            GameInventoryType.ArmoryLegs, GameInventoryType.ArmoryFeets,
            GameInventoryType.ArmoryEar, GameInventoryType.ArmoryNeck,
            GameInventoryType.ArmoryWrist, GameInventoryType.ArmoryRings,
            GameInventoryType.SaddleBag1, GameInventoryType.SaddleBag2,
            GameInventoryType.PremiumSaddleBag1, GameInventoryType.PremiumSaddleBag2
        };

        foreach (var container in containers)
        {
            foreach (var item in GameInventory.GetInventoryItems(container))
            {
                if (!item.IsEmpty && item.BaseItemId != 0)
                    ids.Add(item.BaseItemId);
            }
        }

        var mirageManager = MirageManager.Instance();
        if (mirageManager != null && mirageManager->PrismBoxLoaded)
        {
            var setSheet = DataManager.GetExcelSheet<MirageStoreSetItem>();

            for (var index = 0; index < mirageManager->PrismBoxItemIds.Length; index++)
            {
                var itemId = mirageManager->PrismBoxItemIds[index];
                if (itemId == 0)
                    continue;

                // Since FFXIV 7.4, a dresser entry can represent a whole outfit
                // set. Expand the stored set into the individual pieces used by
                // FFXIV Collect, respecting partial-set ownership.
                if (setSheet.TryGetRow(itemId, out var set))
                {
                    var setItemIds = new[]
                    {
                        set.MainHand.RowId,
                        set.OffHand.RowId,
                        set.Head.RowId,
                        set.Body.RowId,
                        set.Hands.RowId,
                        set.Legs.RowId,
                        set.Feet.RowId,
                        set.Earrings.RowId,
                        set.Necklace.RowId,
                        set.Bracelets.RowId,
                        set.Ring.RowId
                    };

                    for (var slot = 0; slot < setItemIds.Length; slot++)
                    {
                        if (setItemIds[slot] != 0
                            && mirageManager->IsSetSlotUnlocked((uint)index, slot))
                        {
                            ids.Add(setItemIds[slot]);
                        }
                    }

                    continue;
                }

                ids.Add(itemId);
            }
        }

        var uiState = UIState.Instance();
        if (uiState != null && uiState->Cabinet.IsCabinetLoaded())
        {
            foreach (var row in DataManager.GetExcelSheet<Lumina.Excel.Sheets.Cabinet>())
            {
                if (row.Item.RowId != 0 && uiState->Cabinet.IsItemInCabinet(row.RowId))
                    ids.Add(row.Item.RowId);
            }
        }

        return ids;
    }
}

internal sealed record OnlineCollectionData(uint[] OutfitIds, uint[] RelicIds);
internal sealed record OutfitCatalogEntry(uint Id, uint[] ItemIds);
internal sealed record RelicCatalogEntry(uint Id, uint? AchievementId);

internal sealed record ExportResult(
    IReadOnlyDictionary<string, uint[]> Collections,
    IReadOnlyDictionary<string, string> OmittedCollections,
    string Json);
