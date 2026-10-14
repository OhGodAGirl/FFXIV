namespace Exportaramus.Features.CollectionData.Publication;

public sealed class ReporterRateLimiter
{
    private readonly Dictionary<string, Bucket> reporters = new(StringComparer.Ordinal);
    private Bucket global = new(20, 0);

    public bool TryAccept(string reporter, double seconds)
    {
        var globalNow = Refill(global, seconds, 1, 20);
        var reporterNow = reporters.TryGetValue(reporter, out var bucket) ? Refill(bucket, seconds, 0.5, 10) : new Bucket(10, seconds);
        if (globalNow.Tokens < 1 || reporterNow.Tokens < 1) return false;
        // Fully refilled buckets are equivalent to absence and may be removed safely.
        // Expire inactive labels on each acceptance. A label refills completely within 20 seconds;
        // the global burst/rate admits at most 40 distinct labels in that interval.
        foreach (var pair in reporters.ToArray())
            if (Refill(pair.Value, seconds, 0.5, 10).Tokens >= 10) reporters.Remove(pair.Key);
        global = new(globalNow.Tokens - 1, globalNow.At);
        reporters[reporter] = new(reporterNow.Tokens - 1, reporterNow.At);
        return true;
    }

    public void Reset() { reporters.Clear(); global = new Bucket(20, 0); }
    private static Bucket Refill(Bucket bucket, double seconds, double rate, int capacity) =>
        new(Math.Min(capacity, bucket.Tokens + Math.Max(0, seconds - bucket.At) * rate), Math.Max(bucket.At, seconds));
    private sealed record Bucket(double Tokens, double At);
}
