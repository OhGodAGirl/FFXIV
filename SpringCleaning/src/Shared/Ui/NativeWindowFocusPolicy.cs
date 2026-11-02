namespace SpringCleaning;

internal static class NativeWindowFocusPolicy
{
    public static bool IsAnchorFocused(
        nint anchorAddress,
        string anchorName,
        nint trackedAddress,
        nint trackedBaseAddress,
        string trackedName,
        nint rawFocusedAddress,
        nint resolvedFocusedAddress)
    {
        if (anchorAddress == 0)
            return false;

        return anchorAddress == trackedAddress
            || anchorAddress == trackedBaseAddress
            || anchorAddress == rawFocusedAddress
            || anchorAddress == resolvedFocusedAddress
            || (!string.IsNullOrEmpty(anchorName)
                && string.Equals(anchorName, trackedName, StringComparison.Ordinal));
    }
}
