using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.State;
namespace Exportaramus.Features.CollectionData.Publication;

public sealed class ReportedEvidenceQueue
{
    private readonly object sync = new();
    private readonly Dictionary<(uint Id, string Reporter), ReportedEvidence> pending = [];
    private readonly HashSet<(uint Id, string Reporter)> retained = [];
    private readonly ReporterRateLimiter limiter = new();
    private ImmutableHashSet<uint> knownItems = [];
    private ulong activeId;
    private bool stopping;
    private bool healthy;

    public int Count { get { lock (sync) return pending.Count; } }
    public bool Available { get { lock (sync) return healthy && !stopping; } }
    public void SetScope(ulong contentId, ImmutableHashSet<uint> items, IEnumerable<ReportedEvidence> records)
    {
        lock (sync)
        {
            activeId = contentId; knownItems = items; pending.Clear(); retained.Clear(); limiter.Reset();
            foreach (var record in records) retained.Add((record.ItemId, record.Reporter));
            int retainedCount = retained.Count;
            healthy = contentId != 0 && retainedCount <= 10000;
        }
    }

    public string TryEnqueue(string contentId, uint itemId, string reporter, double monotonicSeconds, DateTimeOffset utcNow)
    {
        lock (sync)
        {
            if (stopping) return "ProviderStopping";
            if (!healthy || activeId == 0) return "Unavailable";
            if (!ReportOwnershipEvidencePolicy.TryNormalize(contentId, itemId, reporter, out var character, out var label) || !knownItems.Contains(itemId)) return "InvalidRequest";
            if (character != activeId) return "WrongCharacter";
            var key = (itemId, label);
            int retainedCount = retained.Count;
            if (!retained.Contains(key) && retainedCount >= 10000) return "CapacityReached";
            if (!pending.ContainsKey(key))
            {
                int pendingCount = pending.Count;
                if (pendingCount >= 256) return "QueueFull";
                int reporterCount = pending.Keys.Count(k => k.Reporter == label);
                if (reporterCount >= 32) return "QueueFull";
            }
            if (!limiter.TryAccept(label, monotonicSeconds)) return "RateLimited";
            pending[key] = new(itemId, label, utcNow);
            retained.Add(key); // Reserve accepted keys until the owned persistence attempt completes.
            return "Accepted";
        }
    }

    public ImmutableArray<ReportedEvidence> TakeBatch()
    {
        lock (sync)
        {
            var batch = pending.Values.OrderBy(value => value.ItemId).ThenBy(value => value.Reporter, StringComparer.Ordinal).ToImmutableArray();
            pending.Clear(); return batch;
        }
    }

    public void PersistenceFailed() { lock (sync) healthy = false; }
    public void Stop() { lock (sync) { stopping = true; healthy = false; pending.Clear(); } }
}
