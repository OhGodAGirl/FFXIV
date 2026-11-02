namespace SpringCleaning;

internal static class OutfitSelectionBoundsPolicy
{
    public static int GetVisibleOutfitChoiceCount(int nativeChoiceCount, int tabButtonCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nativeChoiceCount);
        ArgumentOutOfRangeException.ThrowIfNegative(tabButtonCount);
        return Math.Min(nativeChoiceCount, tabButtonCount);
    }

    public static int GetSelectablePieceCount(int nativePieceCount, int nativeItemCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nativePieceCount);
        ArgumentOutOfRangeException.ThrowIfNegative(nativeItemCapacity);
        return Math.Min(nativePieceCount, nativeItemCapacity);
    }
}
