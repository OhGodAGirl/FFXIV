namespace Exportaramus.Features.CollectionData.Capture;

public sealed class StorageWindowEdgePolicy
{
    private bool ready;
    private ulong fingerprint;
    private double lastObserved = double.NegativeInfinity;

    public bool IsDue(double now) => now - lastObserved >= 1;

    public bool Observe(bool isReady, ulong contentFingerprint, double now, bool guidedOwnsEdges)
    {
        if (!IsDue(now)) return false;
        bool request = isReady && (!ready || fingerprint != contentFingerprint);
        ready = isReady; fingerprint = contentFingerprint; lastObserved = now;
        return request && !guidedOwnsEdges;
    }

    public void Reset() { ready = false; fingerprint = 0; lastObserved = double.NegativeInfinity; }
}
