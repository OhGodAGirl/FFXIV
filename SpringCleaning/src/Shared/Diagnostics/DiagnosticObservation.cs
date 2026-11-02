namespace SpringCleaning;

internal readonly record struct DiagnosticObservation(
    DiagnosticObservationKind Kind,
    string? Line)
{
    public static DiagnosticObservation None => new(DiagnosticObservationKind.None, null);
}
