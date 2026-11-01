using Dalamud.Game.Inventory.InventoryEventArgTypes;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace Exportaramus.Features.CollectionData.Capture;

// Platform subscriptions only; classification, coalescing and burst limits are tested separately.
public sealed class PromptCaptureHints : IDisposable
{
    private readonly IUnlockState unlocks;
    private readonly IGameInventory inventory;
    private readonly PromptCaptureHintBuffer hints = new();
    private int disposed;
    public PromptCaptureHints(IUnlockState unlocks, IGameInventory inventory)
    {
        this.unlocks = unlocks;
        this.inventory = inventory;
        unlocks.Unlock += OnUnlock;
        try { inventory.InventoryChangedRaw += OnInventory; }
        catch { unlocks.Unlock -= OnUnlock; throw; }
    }
    public void Drain(Action<string> hint) => hints.Drain(hint);
    public void Reset() => hints.Reset();
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        hints.Dispose();
        try { unlocks.Unlock -= OnUnlock; }
        finally { inventory.InventoryChangedRaw -= OnInventory; }
    }
    private void OnUnlock(RowRef row) => hints.Unlock(
        row.Is<Mount>() ? nameof(Mount) :
            row.Is<Companion>() ? nameof(Companion) :
            row.Is<Emote>() ? nameof(Emote) :
            row.Is<BuddyEquip>() ? nameof(BuddyEquip) :
            row.Is<TripleTriadCard>() ? nameof(TripleTriadCard) :
            row.Is<Ornament>() ? nameof(Ornament) :
            row.Is<BannerCondition>() ? nameof(BannerCondition) :
            row.Is<BannerFrame>() ? nameof(BannerFrame) :
            row.Is<BannerBg>() ? nameof(BannerBg) :
            row.Is<BannerDecoration>() ? nameof(BannerDecoration) :
            row.Is<MKDLore>() ? nameof(MKDLore) :
            row.Is<Orchestrion>() ? nameof(Orchestrion) :
            row.Is<AozAction>() ? nameof(AozAction) :
            row.Is<Aetheryte>() ? nameof(Aetheryte) :
            row.Is<ChocoboTaxiStand>() ? nameof(ChocoboTaxiStand) :
            row.Is<ChocoboTaxi>() ? nameof(ChocoboTaxi) :
            row.Is<Leve>() ? nameof(Leve) :
            row.Is<CharaMakeCustomize>() ? nameof(CharaMakeCustomize) :
            row.Is<Quest>() ? nameof(Quest) :
            row.Is<Achievement>() ? nameof(Achievement) :
            row.Is<Glasses>() ? nameof(Glasses) :
            row.Is<GlassesStyle>() ? nameof(GlassesStyle) :
            row.Is<Item>() ? nameof(Item) :
            row.Is<McGuffin>() ? nameof(McGuffin) : string.Empty);
    private void OnInventory(IReadOnlyCollection<InventoryEventArgs> changes) =>
        hints.Inventory(changes.Select(change => change.Item.ContainerType.ToString()));
}
