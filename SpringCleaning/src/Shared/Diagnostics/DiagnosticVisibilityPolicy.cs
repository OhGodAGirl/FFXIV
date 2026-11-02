namespace SpringCleaning;

internal static class DiagnosticVisibilityPolicy
{
    public static bool ShouldOpen(
        bool dresserOpen,
        bool armoireOpen,
        bool diagnosticMode) =>
        (dresserOpen || armoireOpen) && diagnosticMode;
}
