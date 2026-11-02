namespace SpringCleaning;

internal sealed class NativePhaseDeadline
{
    private MigrationPhase? tracked;
    private DateTime deadline;

    internal bool Expired(MigrationPhase phase, DateTime now)
    {
        if (phase is not (MigrationPhase.ClosingDresser or MigrationPhase.OpeningArmoire
            or MigrationPhase.StoringDresserOutfit))
        {
            Reset();
            return false;
        }
        if (tracked != phase)
        {
            tracked = phase;
            deadline = now.AddSeconds(15);
        }
        return now >= deadline;
    }

    internal void Reset() => tracked = null;
}
