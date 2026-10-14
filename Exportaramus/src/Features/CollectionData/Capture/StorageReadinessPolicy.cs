namespace Exportaramus.Features.CollectionData.Capture;

public sealed class StorageReadinessPolicy
{
    private readonly Dictionary<string, StorageWindowEdgePolicy> edges = new(StringComparer.Ordinal)
    {
        ["glamourDresser"] = new(),
        ["armoire"] = new(),
        ["chocoboSaddlebag"] = new(),
        ["premiumSaddlebag"] = new(),
    };
    public bool Poll(bool identityMatches, Func<string, bool> validated, Func<string, (bool Ready, ulong Fingerprint)> read,
        double monotonicNow, bool guidedOwnsEdges)
    {
        if (!identityMatches) { Reset(); return false; }
        var capture = false;
        foreach (var (key, edge) in edges)
        {
            if (!validated(key)) { edge.Reset(); continue; }
            if (!edge.IsDue(monotonicNow)) continue;
            try
            {
                var (ready, fingerprint) = read(key);
                capture |= edge.Observe(ready, fingerprint, monotonicNow, guidedOwnsEdges);
            }
            catch (Exception) { edge.Observe(false, 0, monotonicNow, guidedOwnsEdges); }
        }
        return capture;
    }
    public void Reset() { foreach (var edge in edges.Values) edge.Reset(); }
}


