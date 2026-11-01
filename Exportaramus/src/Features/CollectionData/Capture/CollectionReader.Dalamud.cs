using System.Collections.Immutable;
using Dalamud.Plugin.Services;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Inventory;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.Exd;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using AchievementRow = Lumina.Excel.Sheets.Achievement;

namespace Exportaramus.Features.CollectionData.Capture;

public sealed unsafe class CollectionReader : ICollectionReader
{
    private readonly IDataManager data;
    private readonly IUnlockState unlock;
    private readonly IPlayerState player;
    private readonly IFramework framework;
    private readonly IGameGui gui;
    private ImmutableDictionary<uint, uint>? fieldRecordItems;
    private ImmutableHashSet<uint>? beastUnlocks;
    private int beastCount;
    private Exportaramus.Features.Inventory.InventorySnapshot? retainerInventory;
    public void SetRetainerInventory(Exportaramus.Features.Inventory.InventorySnapshot? snapshot)
    {
        retainerInventory = snapshot;
    }

    public CollectionReader(IDataManager data, IUnlockState unlock, IPlayerState player, IFramework framework, IGameGui gui)
    {
        this.data = data;
        this.unlock = unlock;
        this.player = player;
        this.framework = framework;
        this.gui = gui;
    }

    public void ResetSession()
    {
        retainerInventory = null;
        beastUnlocks = null;
    }
    public void BeginCapture() => beastUnlocks = null;
    public bool IdentityMatches(ulong contentId) => framework.IsInFrameworkUpdateThread && player.IsLoaded && player.ContentId != 0 && player.ContentId == contentId;
    public bool IsAvailable(string key, string version) => ReaderCompatibility.SourcesFor(version).Contains(key);
    public ImmutableHashSet<string> TrustedSources(string version) => ReaderCompatibility.SourcesFor(version);
    public CollectionReadOperation Begin(CollectionCatalog catalog, bool full, ulong contentId, long generation, DateTimeOffset now)
    {
        beastUnlocks = null;
        return new(this, catalog, full, contentId, generation, now);
    }

    public bool ReadUnlock(string key, CatalogItem row)
    {
        return key switch
        {
            "mounts" => unlock.IsMountUnlocked(Get<Mount>(row.SourceRowId)),
            "minions" => unlock.IsCompanionUnlocked(Get<Companion>(row.SourceRowId)),
            "emotes" => unlock.IsEmoteUnlocked(Get<Emote>(row.SourceRowId)),
            "bardings" => unlock.IsBuddyEquipUnlocked(Get<BuddyEquip>(row.SourceRowId)),
            "cards" => unlock.IsTripleTriadCardUnlocked(Get<TripleTriadCard>(row.SourceRowId)),
            "facewear" => ReadFacewear(row.Id),
            "beasts" => ReadBeast(row.Id),
            "fashions" => unlock.IsOrnamentUnlocked(Get<Ornament>(row.SourceRowId)),
            "frames" => unlock.IsBannerConditionUnlocked(Get<BannerCondition>(row.SourceRowId)),
            "occult_records" => unlock.IsMKDLoreUnlocked(Get<MKDLore>(row.SourceRowId)),
            "orchestrions" => unlock.IsOrchestrionUnlocked(Get<Orchestrion>(row.SourceRowId)),
            "spells" => unlock.IsAozActionUnlocked(Get<AozAction>(row.SourceRowId)),
            "chocobo_routes" => unlock.IsChocoboTaxiStandUnlocked(Get<ChocoboTaxiStand>(row.SourceRowId)),
            "leves" => unlock.IsLeveCompleted(Get<Leve>(row.SourceRowId)),
            "hairstyles" => unlock.IsCharaMakeCustomizeUnlocked(Get<CharaMakeCustomize>(row.SourceRowId)),
            "quests" => unlock.IsQuestCompleted(Get<Quest>(row.SourceRowId)),
            "achievements" => unlock.IsAchievementListLoaded ? unlock.IsAchievementComplete(Get<AchievementRow>(row.SourceRowId)) : throw new SourceReadException(ReasonCode.NotLoadedThisSession),
            "aetherytes" => UIState.Instance() != null ? UIState.Instance()->IsAetheryteUnlocked(row.Id) : throw new SourceReadException(ReasonCode.CaptureFailed),
            "npcs" => UIState.Instance() != null ? UIState.Instance()->IsTripleTriadNpcBeaten(row.Id) : throw new SourceReadException(ReasonCode.CaptureFailed),
            "survey_records" => ReadSurvey(row.Id),
            "field_records" => ReadFieldRecord(row.Id),
            _ => throw new SourceReadException(ReasonCode.MappingValidationFailed),
        };
    }

    public QuestState ReadQuest(uint id, bool completed)
    {
        var manager = QuestManager.Instance();
        if (manager == null) throw new SourceReadException(ReasonCode.CaptureFailed);
        var accepted = manager->IsQuestAccepted(id);
        return NotebookMappingPolicy.Quest(id, completed, accepted, accepted ? QuestManager.GetQuestSequence(id) : null);
    }

    private bool ReadBeast(uint id)
    {
        if (id is < 1 or > BeastReadPolicy.CatalogCount)
            throw new SourceReadException(ReasonCode.MappingValidationFailed);
        var manager = XBMManager.Instance();
        if (manager == null || manager->State != XBMManager.DataState.Received)
            throw new SourceReadException(ReasonCode.NotLoadedThisSession);
        if (beastUnlocks is null)
        {
            var sheet = data.GameData.Excel.GetRawSheet("XBMPet");
            BeastReadPolicy.ValidateCatalog(sheet.Count, sheet.HasRow(0), sheet.HasRow);
            beastCount = manager->NumUnlockedPets;
            // Cache IDs, not native pointers. Clear the cache before each capture.
            var address = (nint)manager;
            beastUnlocks = BeastReadPolicy.Read(true, beastCount,
                value => ((XBMManager*)address)->IsPetUnlocked(value),
                () => XBMManager.Instance() == (XBMManager*)address &&
                    ((XBMManager*)address)->State == XBMManager.DataState.Received &&
                    ((XBMManager*)address)->NumUnlockedPets == beastCount);
        }
        if (manager->NumUnlockedPets != beastCount)
            throw new SourceReadException(ReasonCode.CaptureFailed);
        return beastUnlocks.Contains(id);
    }

    public CurrentItemPresenceState ReadPresence(string source, CollectionCatalog catalog, ulong contentId, long generation, DateTimeOffset now)
    {
        if (!IsAvailable(source, catalog.GameDataVersion))
            return CaptureReadPolicy.FailedPresence(source is not ("armoire" or "glamourDresser"), generation, now, ReasonCode.PatchValidationRequired, false);
        try
        {
            return source switch
            {
                "armoire" => ReadArmoire(catalog, generation, now),
                "glamourDresser" => ReadDresser(catalog, generation, now),
                "retainers" => InventoryRetainerPresencePolicy.Capture(retainerInventory, contentId, true, generation, now),
                _ => ReadContainers(ContainerSets[source], source == "armoryChest", generation, now),
            };
        }
        catch (SourceReadException e) { return CaptureReadPolicy.FailedPresence(source is not ("armoire" or "glamourDresser"), generation, now, e.Code); }
        catch (Exception) { return CaptureReadPolicy.FailedPresence(source is not ("armoire" or "glamourDresser"), generation, now, ReasonCode.CaptureFailed); }
    }

    private CurrentItemPresenceState ReadContainers(InventoryType[] types, bool instances, long generation, DateTimeOffset now)
    {
        var slots = InventoryContainerReadPolicy.Read(new NativeInventoryContainerSource(data), types.Select(type => (int)type).ToArray());
        if (slots is null) throw new SourceReadException(ReasonCode.IncompleteContainerSet);
        return CaptureReadPolicy.Presence(InventoryContainerProjection.Presence(slots, instances), true, true, generation, now);
    }

    private CurrentItemPresenceState ReadArmoire(CollectionCatalog catalog, long generation, DateTimeOffset now)
    {
        var initial = UIState.Instance();
        if (initial == null || !initial->Cabinet.IsCabinetLoaded()) throw new SourceReadException(ReasonCode.WindowNotReady);
        var items = new List<PresenceItem>();
        foreach (var row in catalog.Collections["armoires"].Items)
        {
            var ui = UIState.Instance();
            if (ui == null || ui != initial || !ui->Cabinet.IsCabinetLoaded()) throw new SourceReadException(ReasonCode.WindowNotReady);
            if (ui->Cabinet.IsItemInCabinet(row.SourceRowId)) items.Add(new PresenceItem(row.ItemId!.Value, null));
        }
        if (initial != UIState.Instance() || !initial->Cabinet.IsCabinetLoaded()) throw new SourceReadException(ReasonCode.WindowNotReady);
        return CaptureReadPolicy.Presence(items, false, true, generation, now);
    }

    private CurrentItemPresenceState ReadDresser(CollectionCatalog catalog, long generation, DateTimeOffset now)
    {
        var addon = gui.GetAddonByName<AtkUnitBase>("MiragePrismPrismBox", 1);
        var manager = MirageManager.Instance();
        if (addon == null || !addon->IsReady || !addon->IsVisible || manager == null || !manager->PrismBoxLoaded)
            throw new SourceReadException(ReasonCode.WindowNotReady);
        var entries = new List<DresserEntry>();
        if (manager->PrismBoxItemIds.Length != 800) throw new SourceReadException(ReasonCode.MappingValidationFailed);
        for (var i = 0; i < 800; i++)
        {
            var raw = manager->PrismBoxItemIds[i];
            if (raw == 0) continue;
            var id = DresserSetExpansionPolicy.NormalizeStoredId(raw);
            if (data.GetExcelSheet<MirageStoreSetItem>().HasRow(id))
            {
                var set = Get<MirageStoreSetItem>(id);
                uint[] slots = [set.MainHand.RowId, set.OffHand.RowId, set.Head.RowId, set.Body.RowId, set.Hands.RowId,
                    set.Legs.RowId, set.Feet.RowId, set.Earrings.RowId, set.Necklace.RowId, set.Bracelets.RowId, set.Ring.RowId];
                var unlockedSlots = ImmutableArray.CreateBuilder<bool>(11);
                for (var slot = 0; slot < slots.Length; slot++) unlockedSlots.Add(manager->IsSetSlotUnlocked((uint)i, slot));
                entries.Add(DresserCapturePolicy.Entry(i, raw, catalog.KnownItemIds, [.. slots], unlockedSlots.ToImmutable()));
            }
            else if (catalog.KnownItemIds.Contains(id)) entries.Add(DresserCapturePolicy.Entry(i, raw, catalog.KnownItemIds));
            else throw new SourceReadException(ReasonCode.MappingValidationFailed);
        }
        if (manager != MirageManager.Instance() || !manager->PrismBoxLoaded || addon != gui.GetAddonByName<AtkUnitBase>("MiragePrismPrismBox", 1) || !addon->IsReady || !addon->IsVisible)
            throw new SourceReadException(ReasonCode.WindowNotReady);
        return DresserCapturePolicy.Complete(entries, generation, now);
    }

    private bool ReadFieldRecord(uint id)
    {
        var initial = UIState.Instance();
        var state = PlayerState.Instance();
        if (initial == null || state == null || !state->IsLoaded)
            throw new SourceReadException(ReasonCode.NotLoadedThisSession);
        fieldRecordItems ??= FieldRecordMappingPolicy.CreateMap(data.GetExcelSheet<Item>()
            .Where(r => r.ItemAction.RowId != 0 && r.ItemAction.Value.Action.RowId == 19743)
            .Select(r => ((uint)r.ItemAction.Value.Data[0], r.RowId)));
        if (!fieldRecordItems.TryGetValue(id, out var itemId))
            throw new SourceReadException(ReasonCode.MappingValidationFailed);
        var nativeRow = ExdModule.GetItemRowById(itemId);
        if (nativeRow == null) throw new SourceReadException(ReasonCode.NotLoadedThisSession);
        // The first query may request data from the server. Pending does not mean unowned.
        var result = initial->IsItemActionUnlocked(nativeRow);
        if (initial != UIState.Instance() || state != PlayerState.Instance() || !state->IsLoaded)
            throw new SourceReadException(ReasonCode.CaptureFailed);
        return FieldRecordMappingPolicy.ReadResult(result);
    }

    private bool ReadFacewear(uint id)
    {
        var initial = PlayerState.Instance();
        if (initial == null || !initial->IsLoaded) throw new SourceReadException(ReasonCode.NotLoadedThisSession);
        bool owned = FacewearMappingPolicy.Read(id,
            data.GetExcelSheet<Glasses>().Select(r => new FacewearRow(r.RowId, r.Style.RowId)),
            ReadGlasses);
        if (initial != PlayerState.Instance() || !initial->IsLoaded)
            throw new SourceReadException(ReasonCode.CaptureFailed);
        return owned;
    }

    private static bool ReadGlasses(ushort id)
    {
        var state = PlayerState.Instance();
        if (state == null || !state->IsLoaded) throw new SourceReadException(ReasonCode.NotLoadedThisSession);
        return state->IsGlassesUnlocked(id);
    }

    private bool ReadSurvey(uint id)
    {
        var module = UIModule.Instance();
        if (module == null || module->GetVVDNotebookModule() == null) throw new SourceReadException(ReasonCode.WindowNotReady);
        var notebook = module->GetVVDNotebookModule();
        if (notebook->CharacterContentId != player.ContentId) throw new SourceReadException(ReasonCode.WindowNotReady);
        var index = NotebookMappingPolicy.Resolve(id,
            data.GetExcelSheet<VVDNotebookSeries>().Select(row => new NotebookSeries(row.RowId,
                row.Contents.Select(content => content.RowId).ToImmutableArray())),
            notebook->SeenNotes.Length, notebook->SeenNotes[0].Contents.Length);
        return notebook->SeenNotes[index.Series].Contents[index.Slot] != 0;
    }

    private T Get<T>(uint row) where T : struct, IExcelRow<T> => data.GetExcelSheet<T>().GetRow(row);

    private static readonly Dictionary<string, InventoryType[]> ContainerSets = new(StringComparer.Ordinal)
    {
        ["inventory"] = [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4],
        ["armoryChest"] = [InventoryType.EquippedItems, InventoryType.ArmoryMainHand, InventoryType.ArmoryOffHand,
            InventoryType.ArmoryHead, InventoryType.ArmoryBody, InventoryType.ArmoryHands,
            InventoryType.ArmoryLegs, InventoryType.ArmoryFeets, InventoryType.ArmoryEar, InventoryType.ArmoryNeck,
            InventoryType.ArmoryWrist, InventoryType.ArmoryRings, InventoryType.ArmorySoulCrystal],
        ["chocoboSaddlebag"] = [InventoryType.SaddleBag1, InventoryType.SaddleBag2],
        ["premiumSaddlebag"] = [InventoryType.PremiumSaddleBag1, InventoryType.PremiumSaddleBag2],
    };
}

