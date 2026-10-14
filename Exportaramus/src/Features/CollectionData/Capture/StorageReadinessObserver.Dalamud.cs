using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using CabinetRow = Lumina.Excel.Sheets.Cabinet;

namespace Exportaramus.Features.CollectionData.Capture;

// Readiness observations do not establish supervised validation. Incompatible sources perform no native read.
public sealed unsafe class StorageReadinessObserver(CollectionReader reader, IDataManager data, IGameGui gui, IUnlockState unlocks)
{
    private readonly StorageReadinessPolicy policy = new();
    private readonly StorageWindowEdgePolicy achievements = new();
    private readonly BeastReadinessPolicy beasts = new();
    public bool PollUnlocks(ulong contentId, double now) =>
        achievements.Observe(reader.IdentityMatches(contentId) && unlocks.IsAchievementListLoaded, 0, now, false);

    public bool Poll(ulong contentId, double monotonicNow, bool guidedOwnsEdges)
    {
        var version = data.GameData.Repositories.Values.Single(r => r.ExpansionId == 0).Version;
        return policy.Poll(reader.IdentityMatches(contentId), key => reader.IsAvailable(key, version), Read, monotonicNow, guidedOwnsEdges);
    }
    public bool PollBeasts(ulong contentId, double now)
    {
        var version = data.GameData.Repositories.Values.Single(r => r.ExpansionId == 0).Version;
        return beasts.Poll(reader.IdentityMatches(contentId), reader.IsAvailable("beasts", version), ReadBeasts, now);
    }

    private static (bool, int) ReadBeasts()
    {
        var manager = XBMManager.Instance();
        if (manager == null || manager->State != XBMManager.DataState.Received) return (false, 0);
        int count = manager->NumUnlockedPets;
        return (manager == XBMManager.Instance() && manager->State == XBMManager.DataState.Received && manager->NumUnlockedPets == count, count);
    }

    public void Reset() { policy.Reset(); achievements.Reset(); beasts.Reset(); }

    private (bool, ulong) Read(string key)
    {
        ulong hash = StorageFingerprint.Initial;
        if (key == "glamourDresser")
        {
            var addon = gui.GetAddonByName<AtkUnitBase>("MiragePrismPrismBox", 1);
            var manager = MirageManager.Instance();
            if (addon == null || !addon->IsReady || !addon->IsVisible || manager == null || !manager->PrismBoxLoaded) return (false, 0);
            Span<bool> unlockedSlots = stackalloc bool[11];
            for (var itemIndex = 0; itemIndex < manager->PrismBoxItemIds.Length; itemIndex++)
            {
                var id = manager->PrismBoxItemIds[itemIndex];
                uint normalized = DresserSetExpansionPolicy.NormalizeStoredId(id);
                if (id != 0 && data.GetExcelSheet<MirageStoreSetItem>().HasRow(normalized))
                {
                    for (int slot = 0; slot < 11; slot++)
                        unlockedSlots[slot] = manager->IsSetSlotUnlocked((uint)itemIndex, slot);
                    hash = StorageFingerprint.AddDresserEntry(hash, id, unlockedSlots);
                }
                else hash = StorageFingerprint.AddDresserEntry(hash, id, []);
            }
            return (manager == MirageManager.Instance() && manager->PrismBoxLoaded && addon == gui.GetAddonByName<AtkUnitBase>("MiragePrismPrismBox", 1) && addon->IsReady && addon->IsVisible, hash);
        }
        if (key == "armoire")
        {
            var ui = UIState.Instance();
            if (ui == null || !ui->Cabinet.IsCabinetLoaded()) return (false, 0);
            foreach (var row in data.GetExcelSheet<CabinetRow>())
                if (row.Order != 0 && row.Item.RowId != 0) hash = StorageFingerprint.Add(hash, ui->Cabinet.IsItemInCabinet(row.RowId) ? row.RowId + 1ul : 0);
            return (ui == UIState.Instance() && ui->Cabinet.IsCabinetLoaded(), hash);
        }
        var inventory = InventoryManager.Instance();
        if (inventory == null) return (false, 0);
        InventoryType first = key == "chocoboSaddlebag" ? InventoryType.SaddleBag1 : InventoryType.PremiumSaddleBag1;
        InventoryType second = key == "chocoboSaddlebag" ? InventoryType.SaddleBag2 : InventoryType.PremiumSaddleBag2;
        var a = inventory->GetInventoryContainer(first); var b = inventory->GetInventoryContainer(second);
        return (a != null && b != null && a->IsLoaded && b->IsLoaded, 0);
    }
}
