namespace Exportaramus.Features.CollectionData.Capture;

public sealed class BeastReadinessPolicy
{
    private readonly StorageWindowEdgePolicy edge = new();

    public bool Poll(bool identityMatches, bool compatible, Func<(bool Received, int Count)> read, double now)
    {
        if (!identityMatches || !compatible) { Reset(); return false; }
        if (!edge.IsDue(now)) return false;
        try
        {
            var observation = read();
            bool ready = observation.Received && observation.Count is >= 0 and <= BeastReadPolicy.CatalogCount;
            return edge.Observe(ready, ready ? (ulong)observation.Count : 0, now, false);
        }
        catch (Exception) { edge.Observe(false, 0, now, false); return false; }
    }

    public void Reset() => edge.Reset();
}
