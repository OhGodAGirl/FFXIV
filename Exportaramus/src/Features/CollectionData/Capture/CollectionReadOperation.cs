using Exportaramus.Features.Diagnostics;
using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Features.CollectionData.Capture;

public sealed class CollectionReadOperation : IDisposable
{
    private readonly ICollectionReader reader;
    private readonly DiagnosticMeasurements measurements;
    private readonly string metric;
    private double totalMilliseconds;
    private long totalBytes;
    private readonly CollectionCatalog catalog;
    private readonly ulong contentId;
    private readonly long generation;
    private readonly DateTimeOffset now;
    private readonly IEnumerator<int> steps;
    private readonly ImmutableDictionary<string, PermanentUnlockState>.Builder unlocks = ImmutableDictionary.CreateBuilder<string, PermanentUnlockState>(StringComparer.Ordinal);
    private readonly ImmutableDictionary<string, CurrentItemPresenceState>.Builder presence = ImmutableDictionary.CreateBuilder<string, CurrentItemPresenceState>(StringComparer.Ordinal);
    private readonly ImmutableArray<QuestState>.Builder quests = ImmutableArray.CreateBuilder<QuestState>();
    private bool disposed;

    public CollectionReadOperation(ICollectionReader reader, CollectionCatalog catalog, bool full, ulong contentId, long generation, DateTimeOffset now, DiagnosticMeasurements? measurements = null, ImmutableArray<string> sources = default)
    {
        this.reader = reader; this.catalog = catalog; this.contentId = contentId; this.generation = generation; this.now = now;
        this.measurements = measurements ?? new(); metric = full ? "FullCapture" : "LightweightCapture";
        steps = Read(full, sources).GetEnumerator();
    }

    public RawCaptureBatch? Result { get; private set; }
    public bool Rejected { get; private set; }

    public bool Advance(int maximumRows = 128)
    {
        if (disposed) return true;
        using var timing = new DiagnosticTiming(measurements, metric + "Slice");
        try { return AdvanceCore(maximumRows); }
        finally
        {
            totalMilliseconds += timing.ElapsedMilliseconds; totalBytes += timing.AllocatedBytes;
            if (disposed) measurements.Record(metric, totalMilliseconds, totalBytes);
        }
    }
    private bool AdvanceCore(int maximumRows)
    {
        if (maximumRows is < 1 or > 2048) throw new ArgumentOutOfRangeException(nameof(maximumRows));
        for (int i = 0; i < maximumRows; i++)
        {
            if (!reader.IdentityMatches(contentId)) { Rejected = true; Dispose(); return true; }
            if (!steps.MoveNext())
            {
                if (!reader.IdentityMatches(contentId)) { Rejected = true; Dispose(); return true; }
                Result = new RawCaptureBatch(unlocks.ToImmutable(), presence.ToImmutable(), quests.ToImmutable());
                Dispose(); return true;
            }
        }
        return false;
    }

    public void Dispose() { if (disposed) return; disposed = true; steps.Dispose(); }

    private IEnumerable<int> Read(bool full, ImmutableArray<string> sources)
    {
        if (full || !sources.IsDefault)
            foreach (var definition in catalog.Collections.Values.OrderBy(c => c.Key, StringComparer.Ordinal))
            {
                if (definition.OwnershipKind != DataType.PermanentUnlock) continue;
                if (!sources.IsDefault && !sources.Contains(definition.Key)) continue;
                double sourceMilliseconds = 0; long sourceBytes = 0;
                bool available = reader.IsAvailable(definition.Key, catalog.GameDataVersion);
                var ids = ImmutableArray.CreateBuilder<uint>();
                var questRows = ImmutableArray.CreateBuilder<QuestState>();
                ReasonCode failure = available ? ReasonCode.None : ReasonCode.PatchValidationRequired;
                if (available)
                    foreach (var row in definition.Items)
                    {
                        var queryTiming = new DiagnosticTiming(measurements, "UnlockQuery");
                        try
                        {
                            bool complete = reader.ReadUnlock(definition.Key, row);
                            if (complete) ids.Add(row.Id);
                            if (definition.Key == "quests") questRows.Add(reader.ReadQuest(row.Id, complete));
                        }
                        catch (SourceReadException e) { failure = e.Code; }
                        catch (Exception) { failure = ReasonCode.CaptureFailed; }
                        finally { sourceMilliseconds += queryTiming.ElapsedMilliseconds; sourceBytes += queryTiming.AllocatedBytes; }
                        yield return 0;
                        if (failure != ReasonCode.None) break;
                    }
                if (available) measurements.Record("Source." + definition.Key, sourceMilliseconds, sourceBytes);
                int unlockedCount = ids.Count;
                if (failure == ReasonCode.None && unlockedCount > 50000) failure = ReasonCode.OutputSizeLimitExceeded;
                unlocks[definition.Key] = new PermanentUnlockState(failure == ReasonCode.None ? ids.ToImmutable() : [],
                    CaptureReadPolicy.Health(DataType.PermanentUnlock, generation, now, failure, available));
                if (failure == ReasonCode.None) quests.AddRange(questRows);
                else if (available) measurements.RecordFailure(DiagnosticRedactionPolicy.Failure(DateTime.UtcNow, "Capture", definition.Key, failure.ToString(), null));
                yield return 0;
            }
        foreach (var source in PhysicalSources)
        {
            if (!sources.IsDefault && !sources.Contains(source)) continue;
            presence[source] = measurements.Measure("Source." + source, () => reader.ReadPresence(source, catalog, contentId, generation, now));
            yield return 0;
        }
    }

    public static readonly ImmutableArray<string> PhysicalSources =
        ["inventory", "armoryChest", "chocoboSaddlebag", "premiumSaddlebag", "retainers", "armoire", "glamourDresser"];
}
