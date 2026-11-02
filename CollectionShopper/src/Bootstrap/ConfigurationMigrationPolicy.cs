namespace CollectionShopper;

internal static class ConfigurationMigrationPolicy
{
    internal const int CurrentVersion = 3;
    internal const int DefaultActionDelayMilliseconds = 600;
    internal const int LegacyActionDelayMilliseconds = 1_250;

    internal static (int Version, int ActionDelayMilliseconds, bool Changed) Evaluate(int version, int actionDelayMilliseconds)
    {
        if (version >= CurrentVersion) return (version, actionDelayMilliseconds, false);
        var delay = version < 2 && actionDelayMilliseconds == LegacyActionDelayMilliseconds
            ? DefaultActionDelayMilliseconds : actionDelayMilliseconds;
        return (CurrentVersion, delay, true);
    }
}
