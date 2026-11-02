namespace SpringCleaning;

// Temporary, bounded observation of presentation only. No ownership or action decisions use it.
internal sealed class UiFlickerTrace
{
    private const int MaximumReports = 120;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SummaryInterval = TimeSpan.FromSeconds(10);
    private DateTime? startedAt;
    private DateTime nextSummary;
    private UiFlickerState? previous;
    private int previousFrame;
    private int reports;

    public bool Finished { get; private set; }

    public string? Observe(DateTime now, int frame, UiFlickerState state, long updates, long refreshes)
    {
        if (Finished || (startedAt is null && state.OpenBefore == 0))
            return null;

        startedAt ??= now;
        if (now - startedAt.Value >= Lifetime || reports >= MaximumReports)
        {
            Finished = true;
            return "UI trace finished (time/report limit).";
        }

        // ImGui frame differences identify omitted callbacks without mistaking low FPS for omissions.
        var skippedFrames = previous is null ? 0 : Math.Max(0, frame - previousFrame - 1);
        var changed = previous != state;
        previous = state;
        previousFrame = frame;
        if (!changed && skippedFrames == 0 && now < nextSummary)
            return null;

        nextSummary = now + SummaryInterval;
        reports++;
        return $"UI trace frame={frame} skipped={skippedFrames} updates={updates} refreshes={refreshes} "
            + $"open={state.OpenBefore:X2}/{state.OpenAfter:X2} drawn={state.Drawn:X2} "
            + $"dim={state.Dim:X2} zero={state.Zero:X2} entryAlpha={state.EntryAlpha:F3} "
            + $"exitAlpha={state.ExitAlpha:F3} native={state.NativeVisible:X2} "
            + $"snapshot={state.SnapshotAvailable} hide={state.HideEvents} show={state.ShowEvents}.";
    }
}

internal readonly record struct UiFlickerState(
    int OpenBefore,
    int OpenAfter,
    int Drawn,
    int Dim,
    int Zero,
    float EntryAlpha,
    float ExitAlpha,
    int NativeVisible,
    bool SnapshotAvailable,
    int HideEvents,
    int ShowEvents);
