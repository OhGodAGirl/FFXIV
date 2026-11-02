namespace SpringCleaning;

internal sealed class DiagnosticActivityLog
{
    private readonly int capacity;
    private readonly List<string> entries = [];
    private string? lastFingerprint;
    private DateTime stateEnteredAt;
    private DateTime lastWaitingNoticeAt;
    private static readonly TimeSpan WaitingNoticeInterval = TimeSpan.FromSeconds(15);

    public DiagnosticActivityLog(int capacity = 150)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        this.capacity = capacity;
    }

    public IReadOnlyList<string> Entries => entries;

    public DiagnosticObservation Observe(
        DateTime timestamp,
        MigrationDiagnosticSnapshot snapshot)
    {
        if (!string.Equals(lastFingerprint, snapshot.Fingerprint, StringComparison.Ordinal))
        {
            lastFingerprint = snapshot.Fingerprint;
            stateEnteredAt = timestamp;
            lastWaitingNoticeAt = timestamp;
            return Add(timestamp, DiagnosticObservationKind.Transition, snapshot.ToActivitySummary());
        }

        var stateAge = timestamp - stateEnteredAt;
        if (!snapshot.IsActive
            || timestamp - lastWaitingNoticeAt < WaitingNoticeInterval)
        {
            return DiagnosticObservation.None;
        }

        lastWaitingNoticeAt = timestamp;
        return Add(
            timestamp,
            DiagnosticObservationKind.Waiting,
            $"STILL WAITING after {stateAge.TotalSeconds:N0}s | {snapshot.ToActivitySummary()}");
    }

    public void Clear() => entries.Clear();

    private DiagnosticObservation Add(
        DateTime timestamp,
        DiagnosticObservationKind kind,
        string message)
    {
        var line = $"[{timestamp:HH:mm:ss}] {message}";
        entries.Add(line);
        entries.RemoveRange(0, Math.Max(0, entries.Count - capacity));
        return new DiagnosticObservation(kind, line);
    }
}
