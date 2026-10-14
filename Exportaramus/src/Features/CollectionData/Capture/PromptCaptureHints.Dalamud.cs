using Dalamud.Game.Inventory;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace Exportaramus.Features.CollectionData.Capture;

// Events only enqueue bounded source hints. Drain is called on the framework thread.
public sealed class PromptCaptureHints : IDisposable
{
    private static readonly string[] Sources =
        ["mounts", "minions", "emotes", "bardings", "cards", "fashions", "frames",
         "occult_records", "orchestrions", "spells", "aetherytes", "chocobo_routes",
         "leves", "hairstyles", "field_records", "survey_records", "quests", "achievements",
         "facewear", "inventory", "armoryChest", "chocoboSaddlebag", "premiumSaddlebag", "retainers"];
    private const int UnlockMask = (1 << 19) - 1;
    private const int PresenceMask = ((1 << 24) - 1) & ~UnlockMask;
    private readonly IUnlockState unlocks;
    private readonly IGameInventory inventory;
    private int pending;
    private int disposed;

    public PromptCaptureHints(IUnlockState unlocks, IGameInventory inventory)
    {
        this.unlocks = unlocks;
        this.inventory = inventory;
        unlocks.Unlock += OnUnlock;
        try { inventory.InventoryChangedRaw += OnInventory; }
        catch { unlocks.Unlock -= OnUnlock; throw; }
    }

    public void Drain(Action<string> hint)
    {
        int mask = Interlocked.Exchange(ref pending, 0);
        if (Volatile.Read(ref disposed) != 0) return;
        for (int i = 0; i < Sources.Length; i++)
            if ((mask & (1 << i)) != 0) hint(Sources[i]);
    }

    public void Reset() => Interlocked.Exchange(ref pending, 0);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try { unlocks.Unlock -= OnUnlock; }
        finally { inventory.InventoryChangedRaw -= OnInventory; Reset(); }
    }

    private void Queue(int mask)
    {
        if (Volatile.Read(ref disposed) == 0) Interlocked.Or(ref pending, mask);
    }

    private void OnUnlock(RowRef row)
    {
        int index = row.Is<Mount>() ? 0 : row.Is<Companion>() ? 1 : row.Is<Emote>() ? 2 :
            row.Is<BuddyEquip>() ? 3 : row.Is<TripleTriadCard>() ? 4 : row.Is<Ornament>() ? 5 :
            row.Is<BannerCondition>() || row.Is<BannerFrame>() || row.Is<BannerBg>() || row.Is<BannerDecoration>() ? 6 :
            row.Is<MKDLore>() ? 7 : row.Is<Orchestrion>() ? 8 : row.Is<AozAction>() ? 9 :
            row.Is<Aetheryte>() ? 10 : row.Is<ChocoboTaxiStand>() || row.Is<ChocoboTaxi>() ? 11 :
            row.Is<Leve>() ? 12 : row.Is<CharaMakeCustomize>() ? 13 :
            row.Is<Quest>() ? 16 : row.Is<Achievement>() ? 17 : row.Is<Glasses>() || row.Is<GlassesStyle>() ? 18 : -1;
        if (index >= 0) Queue(1 << index);
        else if (row.Is<Item>() || row.Is<McGuffin>()) Queue(UnlockMask);
    }

    private void OnInventory(IReadOnlyCollection<InventoryEventArgs> changes)
    {
        int mask = 0, count = 0;
        foreach (var change in changes)
        {
            // Large login/container bursts collapse into a fixed set instead of unbounded queued work.
            if (++count > 256) { mask |= PresenceMask | UnlockMask; break; }
            int index = change.Item.ContainerType switch
            {
                GameInventoryType.Inventory1 or GameInventoryType.Inventory2 or GameInventoryType.Inventory3 or
                    GameInventoryType.Inventory4 or GameInventoryType.Crystals or GameInventoryType.Currency => 19,
                GameInventoryType.EquippedItems or GameInventoryType.ArmoryMainHand or GameInventoryType.ArmoryOffHand or
                    GameInventoryType.ArmoryHead or GameInventoryType.ArmoryBody or GameInventoryType.ArmoryHands or
                    GameInventoryType.ArmoryLegs or GameInventoryType.ArmoryFeets or GameInventoryType.ArmoryEar or
                    GameInventoryType.ArmoryNeck or GameInventoryType.ArmoryWrist or GameInventoryType.ArmoryRings or
                    GameInventoryType.ArmorySoulCrystal => 20,
                GameInventoryType.SaddleBag1 or GameInventoryType.SaddleBag2 => 21,
                GameInventoryType.PremiumSaddleBag1 or GameInventoryType.PremiumSaddleBag2 => 22,
                GameInventoryType.RetainerPage1 or GameInventoryType.RetainerPage2 or GameInventoryType.RetainerPage3 or
                    GameInventoryType.RetainerPage4 or GameInventoryType.RetainerPage5 or GameInventoryType.RetainerPage6 or
                    GameInventoryType.RetainerPage7 or GameInventoryType.RetainerEquippedItems or
                    GameInventoryType.RetainerCrystals or GameInventoryType.RetainerGil or GameInventoryType.RetainerMarket => 23,
                _ => -1,
            };
            if (index >= 0) mask |= 1 << index;
        }
        Queue(mask);
    }
}
