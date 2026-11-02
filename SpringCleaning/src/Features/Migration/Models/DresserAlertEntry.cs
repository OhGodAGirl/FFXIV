namespace SpringCleaning;

internal sealed record DresserAlertEntry(
    int PrismBoxIndex,
    uint RawItemId,
    uint ItemId,
    bool IsHighQuality,
    bool IsOutfit,
    uint ItemCategoryId,
    string ItemCategoryName,
    string Name,
    uint IconId,
    DresserAlertKind Kind,
    bool IsAlreadyInArmoire,
    bool HasMixedQualitySet,
    bool BelongsToOutfit)
{
    internal IReadOnlyList<SpringCleaning.Shared.CollectionData.CollectionDresserComponent> Components { get; init; } = [];
}
