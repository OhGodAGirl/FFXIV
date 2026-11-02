namespace SpringCleaning;

internal sealed record EligibleDresserItem(
    int PrismBoxIndex,
    uint RawItemId,
    string Name,
    IReadOnlyList<EligibleDresserComponent> Components)
{
    public bool IsOutfit => Components.Any(component => component.OutfitSlot >= 0);

    public int InventorySlotsRequired => Components.Count;
}
