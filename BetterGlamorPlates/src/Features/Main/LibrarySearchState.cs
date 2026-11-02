namespace BetterGlamorPlates.Features.Main;

/// <summary>Preserves newer typing while queued search commands are acknowledged.</summary>
internal sealed class LibrarySearchState
{
    private string observed = string.Empty;
    private int observedCount;
    internal string? Pending { get; set; }

    internal string? Reconcile(string acknowledged, int plateCount)
    {
        if (observed == acknowledged && observedCount == plateCount) return null;
        string? replacement = null;
        if (Pending is null || Pending == acknowledged || observedCount != plateCount)
        {
            replacement = acknowledged;
            Pending = null;
        }
        observed = acknowledged;
        observedCount = plateCount;
        return replacement;
    }
}
