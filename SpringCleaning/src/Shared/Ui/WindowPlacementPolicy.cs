namespace SpringCleaning;

internal static class WindowPlacementPolicy
{
    private const int InitialSnapMilliseconds = 1_500;

    public static bool ShouldSnapAtSessionStart(bool rememberWindowPlacement)
    {
        return !rememberWindowPlacement;
    }

    public static float GetScaledPanelWidth(float logicalWidth, float globalScale)
    {
        return logicalWidth * MathF.Max(0.1f, globalScale);
    }

    public static float GetGlamourCreationX(
        float dresserSidecarX,
        float gap,
        bool diagnosticsOpen,
        float diagnosticWidth,
        bool settingsOpen,
        float settingsWidth,
        bool migrationOpen,
        float migrationWidth)
    {
        var sharedColumnWidth = MathF.Max(
            diagnosticsOpen ? diagnosticWidth : 0f,
            settingsOpen ? settingsWidth : 0f);
        var outerX = sharedColumnWidth > 0f
            ? dresserSidecarX + sharedColumnWidth + gap
            : dresserSidecarX;
        return migrationOpen
            ? outerX + migrationWidth + gap
            : outerX;
    }

    public static DateTime GetInitialSnapDeadline(DateTime now)
    {
        return now.AddMilliseconds(InitialSnapMilliseconds);
    }

    public static bool ShouldApplyInitialSnap(DateTime now, DateTime deadline)
    {
        return now <= deadline;
    }
}
