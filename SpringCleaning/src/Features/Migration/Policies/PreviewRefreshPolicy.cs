namespace SpringCleaning;

internal static class PreviewRefreshPolicy
{
    public static bool ShouldRefresh(
        DateTime now,
        DateTime notBefore,
        bool previewVisible,
        bool operationActive) =>
        previewVisible && !operationActive && now >= notBefore;

    public static DateTime GetNextRefresh(DateTime now, TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, TimeSpan.Zero);
        return now.Add(interval);
    }
}
