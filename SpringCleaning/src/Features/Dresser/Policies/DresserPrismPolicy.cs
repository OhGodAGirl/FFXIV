namespace SpringCleaning;

internal static class DresserPrismPolicy
{
    // Item row 21800 is Glamour Prism in the admitted installed game data.
    internal const uint ItemId = 21_800;

    public static bool IsReliablePreflightCount(uint held) => held > 0;

    public static uint ResolveHeld(
        uint inventoryCount,
        uint trackedCurrencyCount,
        uint? nativeGlamourCreationCount = null) =>
        Math.Max(
            Math.Max(inventoryCount, trackedCurrencyCount),
            nativeGlamourCreationCount.GetValueOrDefault());

    public static DresserPrismAvailability Evaluate(uint held, int required, bool isReady)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(required);
        return new DresserPrismAvailability(held, required, isReady);
    }

    public static string FormatBanner(DresserPrismAvailability availability)
    {
        if (!availability.IsReady)
            return "CHECKING";

        if (availability.Required == 0)
            return "IDLE";

        if (availability.HasEnough)
            return $"READY · USES {availability.Required:N0}";

        return $"NEED {availability.Missing:N0} PRISM"
            + (availability.Missing == 1 ? string.Empty : "S");
    }

    public static string FormatProgress(int stored, int total)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stored);
        ArgumentOutOfRangeException.ThrowIfNegative(total);
        return total == 0 ? "WORKING" : $"STORING · {stored:N0}/{total:N0}";
    }
}
