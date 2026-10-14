using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using CabinetRow = Lumina.Excel.Sheets.Cabinet;

namespace Exportaramus.ApiContractTests;

// Compile-only contract: never invoke native members in the offline executable.
internal static unsafe class ApiContractProbe
{
    internal static void Verify(IDataManager data, IUnlockState unlock, IPlayerState player,
        IFramework framework, IGameGui gui, IDalamudPluginInterface pi, AtkUnitBase* addon)
    {
        _ = player.IsLoaded; _ = player.ContentId; _ = player.CharacterName;
        _ = framework.IsInFrameworkUpdateThread;
        _ = data.Language; _ = data.Excel; _ = data.GameData.Repositories.Values;
        foreach (var repository in data.GameData.Repositories.Values) { _ = repository.ExpansionId; _ = repository.Version; }
        _ = unlock.IsMountUnlocked(default(Mount));
        _ = unlock.IsCompanionUnlocked(default(Companion));
        _ = unlock.IsEmoteUnlocked(default(Emote));
        _ = unlock.IsBuddyEquipUnlocked(default(BuddyEquip));
        _ = unlock.IsTripleTriadCardUnlocked(default(TripleTriadCard));
        _ = unlock.IsOrnamentUnlocked(default(Ornament));
        _ = unlock.IsBannerConditionUnlocked(default(BannerCondition));
        _ = unlock.IsBannerFrameUnlocked(default(BannerFrame));
        _ = unlock.IsMKDLoreUnlocked(default(MKDLore));
        _ = unlock.IsOrchestrionUnlocked(default(Orchestrion));
        _ = unlock.IsAozActionUnlocked(default(AozAction));
        _ = unlock.IsChocoboTaxiStandUnlocked(default(ChocoboTaxiStand));
        _ = unlock.IsLeveCompleted(default(Leve));
        _ = unlock.IsCharaMakeCustomizeUnlocked(default(CharaMakeCustomize));
        _ = unlock.IsQuestCompleted(default(Quest));
        _ = unlock.IsAchievementListLoaded; _ = unlock.IsAchievementComplete(default(Lumina.Excel.Sheets.Achievement));
        _ = UIState.Instance()->IsAetheryteUnlocked(1);
        _ = UIState.Instance()->IsTripleTriadNpcBeaten(2293762);
        _ = QuestManager.Instance()->IsQuestAccepted(1);
        _ = QuestManager.IsQuestComplete(1); _ = QuestManager.GetQuestSequence(1);
        _ = UIState.Instance()->Cabinet.IsCabinetLoaded();
        _ = UIState.Instance()->Cabinet.IsItemInCabinet(0);
        _ = UIState.Instance()->Achievement.IsLoaded();
        var mirage = MirageManager.Instance();
        _ = mirage->PrismBoxLoaded; _ = mirage->PrismBoxItemIds[799];
        _ = mirage->IsSetSlotUnlocked(1, 0);
        _ = UIModule.Instance()->GetMycNoteModule()->Unk48[0];
        _ = UIModule.Instance()->GetMycNoteModule()->Unk80[0];
        _ = UIModule.Instance()->GetVVDNotebookModule()->SeenNotes[0];
        var inventory = InventoryManager.Instance();
        foreach (var type in Containers)
        {
            var container = inventory->GetInventoryContainer(type);
            _ = container->IsLoaded; _ = container->Size;
            var slot = container->GetInventorySlot(0);
            _ = slot->GetBaseItemId(); _ = slot->GetQuantity();
        }
        var retainers = RetainerManager.Instance();
        _ = retainers->IsReady; _ = retainers->GetRetainerCount();
        var retainer = retainers->GetRetainerBySortedIndex(0);
        _ = retainer->RetainerId; _ = retainer->Available; _ = retainers->GetActiveRetainer();
        _ = gui.GetAddonByName<AtkUnitBase>("MiragePrismPrismBox", 1); _ = gui.GetUIModule();
        _ = addon->IsReady; _ = addon->IsVisible; bool closeRequest = addon->Close(true); _ = closeRequest;
        _ = TargetSystem.Instance()->SetHardTarget(null, false, false, 0);
        _ = TargetSystem.Instance()->InteractWithObject(null, false);
        var read = pi.GetIpcProvider<string>("Exportaramus.CollectionData.GetSnapshot");
        read.RegisterFunc(() => ""); read.UnregisterFunc();
        pi.GetIpcProvider<string, object?>("Exportaramus.CollectionData.SnapshotChanged").SendMessage("");
        var report = pi.GetIpcProvider<string, uint, string, string>("Exportaramus.CollectionData.ReportOwnershipEvidence");
        report.RegisterFunc((_, _, _) => "Unavailable"); report.UnregisterFunc();
        _ = pi.GetIpcSubscriber<string>("Exportaramus.CollectionData.GetSnapshot").InvokeFunc();
        var changed = pi.GetIpcSubscriber<string, object?>("Exportaramus.CollectionData.SnapshotChanged");
        System.Action<string> handler = _ => { }; changed.Subscribe(handler); changed.Unsubscribe(handler);
        _ = pi.GetIpcSubscriber<string, uint, string, string>("Exportaramus.CollectionData.ReportOwnershipEvidence").InvokeFunc("1", 1, "Probe");
    }

    private static readonly InventoryType[] Containers =
    [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
        InventoryType.EquippedItems, InventoryType.ArmoryMainHand, InventoryType.ArmoryOffHand,
        InventoryType.ArmoryHead, InventoryType.ArmoryBody, InventoryType.ArmoryHands, InventoryType.ArmoryWaist,
        InventoryType.ArmoryLegs, InventoryType.ArmoryFeets, InventoryType.ArmoryEar, InventoryType.ArmoryNeck,
        InventoryType.ArmoryWrist, InventoryType.ArmoryRings, InventoryType.ArmorySoulCrystal,
        InventoryType.SaddleBag1, InventoryType.SaddleBag2, InventoryType.PremiumSaddleBag1,
        InventoryType.PremiumSaddleBag2, InventoryType.RetainerPage1, InventoryType.RetainerPage2,
        InventoryType.RetainerPage3, InventoryType.RetainerPage4, InventoryType.RetainerPage5,
        InventoryType.RetainerPage6, InventoryType.RetainerPage7, InventoryType.RetainerEquippedItems];
}
