using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed class DiagnosticMeasurements
{
    public const int SampleCapacity = 256;
    public const int FailureCapacity = 20;
    private readonly Dictionary<string, Queue<(double Duration, double Bytes)>> samples = new(StringComparer.Ordinal);
    private readonly Queue<DiagnosticFailure> failures = new();
    private readonly Dictionary<string, double> latest = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private long accepted;
    private long rejected;
    private long throttled;
    private volatile bool hasUnrepresentableSamples;
    public bool HasUnrepresentableSamples => hasUnrepresentableSamples;
    public void Record(string metric, double milliseconds, double allocatedBytes)
    {
        if (milliseconds > 3600000 || allocatedBytes > 3600000) hasUnrepresentableSamples = true;
        if (!DiagnosticRedactionPolicy.IsSymbol(metric) || !double.IsFinite(milliseconds) || !double.IsFinite(allocatedBytes) || milliseconds < 0 || allocatedBytes < 0 || milliseconds > 3600000 || allocatedBytes > 3600000)
            return;
        lock (gate)
        {
            if (!samples.TryGetValue(metric, out var buffer))
            {
                int metricCount = samples.Count;
                if (metricCount == 64) return;
                samples.Add(metric, buffer = new(SampleCapacity));
            }
            int sampleCount = buffer.Count;
            if (sampleCount == SampleCapacity) buffer.Dequeue();
            buffer.Enqueue((milliseconds, allocatedBytes));
            latest[metric] = milliseconds;
        }
    }
    public void RecordFailure(DiagnosticFailure failure)
    {
        failure = DiagnosticRedactionPolicy.Failure(failure.OccurredAtUtc, failure.Component, failure.Operation, failure.Code, failure.ExceptionType);
        lock (gate)
        {
            int failureCount = failures.Count;
            if (failureCount == FailureCapacity) failures.Dequeue();
            failures.Enqueue(failure);
        }
    }
    public void CountReport(bool wasAccepted, bool wasThrottled)
    {
        lock (gate)
        {
            if (wasAccepted) accepted++; else rejected++;
            if (wasThrottled) throttled++;
        }
    }
    public DiagnosticCounters Counters { get { lock (gate) return new(accepted, rejected, throttled); } }
    public ImmutableArray<DiagnosticFailure> Failures { get { lock (gate) return failures.ToImmutableArray(); } }
    public ImmutableDictionary<string, double> LatestMilliseconds { get { lock (gate) return latest.ToImmutableDictionary(StringComparer.Ordinal); } }
    public void Failure(string component, string operation, string code, Exception exception) =>
        RecordFailure(DiagnosticRedactionPolicy.Failure(DateTime.UtcNow, component, operation, code, exception.GetType().Name));
    public T Measure<T>(string metric, Func<T> action)
    {
        using var timing = new DiagnosticTiming(this, metric);
        try { return action(); }
        catch (Exception exception) { Failure("Pipeline", metric, "OperationFailed", exception); throw; }
    }
    public ImmutableDictionary<string, DiagnosticMetric> Snapshot()
    {
        KeyValuePair<string, (double Duration, double Bytes)[]>[] captured;
        lock (gate) captured = samples.Select(x => KeyValuePair.Create(x.Key, x.Value.ToArray())).ToArray();
        return captured.ToImmutableDictionary(x => x.Key,
            x => new DiagnosticMetric(x.Value.Length, Percentiles(x.Value.Select(v => v.Duration)), Percentiles(x.Value.Select(v => v.Bytes))), StringComparer.Ordinal);
    }
    public void ResetMeasurements()
    {
        lock (gate) { samples.Clear(); latest.Clear(); hasUnrepresentableSamples = false; accepted = rejected = throttled = 0; }
    }
    public static DiagnosticPercentiles Percentiles(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0) return new(0, 0, 0);
        return new(sorted[(int)Math.Ceiling(sorted.Length * .50) - 1], sorted[(int)Math.Ceiling(sorted.Length * .95) - 1], sorted[^1]);
    }
}
