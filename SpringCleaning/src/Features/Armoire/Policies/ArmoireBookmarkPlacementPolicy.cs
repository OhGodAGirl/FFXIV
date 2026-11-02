namespace SpringCleaning;

internal static class ArmoireBookmarkPlacementPolicy
{
    public static float GetLeft(float armoireLeft, float totalTabWidth, float frameGap)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalTabWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(frameGap);
        return armoireLeft - totalTabWidth - frameGap;
    }
}
