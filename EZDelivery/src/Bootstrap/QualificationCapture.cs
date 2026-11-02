using System.Diagnostics;
using System.Text.Json;

namespace EZDelivery.Bootstrap;

// Opt-in measurement. Fixed arrays are allocated once per plugin instance.
// No game state, pointer, item or character data is captured.
internal sealed class QualificationCapture
{
    internal const int WarmupCount = 512;
    internal const int SampleCapacity = 10_000;
    private readonly object sync = new();
    private readonly long[] update = new long[SampleCapacity];
    private readonly long[] draw = new long[SampleCapacity];
    private int updateSeen;
    private int drawSeen;
    private int updateCount;
    private int drawCount;
    private long updateAfterCapacity;
    private long drawAfterCapacity;
    private bool active;
    private bool exists;
    private bool exported;
    private long startedTicks;
    private string scenario = "";
    private DateTimeOffset started;
    private DateTimeOffset? ended;
    private readonly string instanceId = Guid.NewGuid().ToString("D");

    internal string InstanceId => instanceId;

    internal bool Start(string name)
    {
        if (name.Length is < 1 or > 64 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')) return false;
        lock (sync)
        {
            if (active || (exists && !exported)) return false;
            updateSeen = drawSeen = updateCount = drawCount = 0;
            updateAfterCapacity = drawAfterCapacity = 0;
            scenario = name;
            started = DateTimeOffset.UtcNow;
            ended = null;
            startedTicks = Stopwatch.GetTimestamp();
            exported = false;
            exists = active = true;
            return true;
        }
    }

    internal long Begin()
    {
        return Volatile.Read(ref active) ? Stopwatch.GetTimestamp() : 0;
    }

    internal void End(bool isDraw, long timestamp)
    {
        if (timestamp == 0) return;
        lock (sync)
        {
            if (timestamp < startedTicks) return;
            Record(isDraw, Stopwatch.GetTimestamp() - timestamp);
        }
    }

    // Receives elapsed Stopwatch ticks; explicit input permits exact host tests.
    internal void Record(bool isDraw, long ticks)
    {
        lock (sync)
        {
            if (!active || ticks < 0) return;
            ref int seen = ref (isDraw ? ref drawSeen : ref updateSeen);
            ref int count = ref (isDraw ? ref drawCount : ref updateCount);
            if (count == SampleCapacity)
            {
                if (isDraw) drawAfterCapacity++; else updateAfterCapacity++;
                return;
            }
            if (seen++ < WarmupCount) return;
            (isDraw ? draw : update)[count++] = ticks;
            if (updateCount == SampleCapacity && drawCount == SampleCapacity)
            {
                active = false;
                ended = DateTimeOffset.UtcNow;
            }
        }
    }

    internal void Stop()
    {
        lock (sync)
        {
            if (!active) return;
            active = false;
            ended = DateTimeOffset.UtcNow;
        }
    }

    internal string Export(string directory, string version, string buildId)
    {
        object snapshot;
        lock (sync)
        {
            if (!exists) throw new InvalidOperationException("Start a qualification capture first.");
            if (active) throw new InvalidOperationException("Capture is still running. Wait for completion or stop it before export.");
            snapshot = new
            {
                SchemaVersion = 1,
                PluginVersion = version,
                LoadedBuildId = buildId,
                InstanceId = instanceId,
                Scenario = scenario,
                StartedAtUtc = started,
                EndedAtUtc = ended,
                StopwatchFrequency = Stopwatch.Frequency,
                WarmupCallbacksPerStream = WarmupCount,
                RequiredSamplesPerStream = SampleCapacity,
                Complete = updateCount == SampleCapacity && drawCount == SampleCapacity,
                CapturePolicy = "First 10000 samples per stream after 512 warmup callbacks. Completed streams stop sampling; after both complete no further callbacks are counted.",
                UpdateCallbacksIgnoredAfterCapacity = updateAfterCapacity,
                DrawCallbacksIgnoredAfterCapacity = drawAfterCapacity,
                MeasurementScope = "Plugin Update and Draw callbacks, including fault reconciliation. CPU callback elapsed time; not GPU or whole-frame time. Export and command processing excluded.",
                Update = Summarize(update, updateCount, updateSeen, 2),
                Draw = Summarize(draw, drawCount, drawSeen, 1)
            };
            // Keep export and start mutually exclusive. Export only runs after
            // capture has stopped, outside measured callbacks.
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"qualification-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json");
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                JsonSerializer.Serialize(stream, snapshot, new JsonSerializerOptions { WriteIndented = true });
            exported = true;
            return path;
        }
    }

    private static object Summarize(long[] values, int count, int seen, double target)
    {
        var samples = values[..count];
        var sorted = samples.Order().ToArray();
        double? Percentile(double fraction) => count == 0 ? null : sorted[(int)Math.Ceiling(count * fraction) - 1] * 1000.0 / Stopwatch.Frequency;
        var p95 = Percentile(.95);
        return new
        {
            SampleCount = count,
            WarmupObserved = Math.Min(seen, WarmupCount),
            RawStopwatchTicks = samples,
            P50Milliseconds = Percentile(.5),
            P95Milliseconds = p95,
            P99Milliseconds = Percentile(.99),
            MaximumMilliseconds = Percentile(1),
            SamplesAtLeast50Milliseconds = samples.Count(x => x * 1000.0 / Stopwatch.Frequency >= 50),
            P95TargetMilliseconds = target,
            MeetsTarget = count == SampleCapacity && p95 <= target
        };
    }
}
