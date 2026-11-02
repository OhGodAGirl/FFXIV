namespace SpringCleaning;

internal readonly record struct DresserPrismAvailability(
    uint Held,
    int Required,
    bool IsReady)
{
    public bool HasEnough => IsReady && Held >= Required;

    public int Missing => IsReady ? Math.Max(0, Required - (int)Held) : 0;
}
