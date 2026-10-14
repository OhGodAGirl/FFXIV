using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Diagnostics;

namespace Exportaramus.Features.Snapshots;

public sealed class SnapshotController : IDisposable
{
    private readonly ISnapshotCapturePort capture;
    private readonly Func<Func<string>, Task<string>> prepareCapabilities;
    private readonly DiagnosticMeasurements measurements;
    private double operationStarted;
    private bool staleCompletionRejected;
    private readonly AtomicSnapshotStorage storage;
    private readonly CollectionDataPublisher publisher;
    private readonly SnapshotSchedulePolicy schedule = new();
    private readonly PromptCapturePolicy prompt = new();
    private bool targeted;
    private bool captureError;
    private readonly SnapshotWithdrawalPolicy identity = new();
    private readonly ChangedNoticeCoalescer notices = new();
    private CancellationTokenSource scope = new();
    private Task<WorkerResult>? worker;
    private Task<string>? capabilityPreparation;
    private bool persistingReports;
    private long workerGeneration;
    private CollectionState? state;
    private CaptureRequest activeRequest;
    private bool reading;
    private bool restored;
    private bool disposed;
    private long generation;
    private long revision;
    private string displayName = string.Empty;
    public Guid Session { get; } = Guid.NewGuid();
    public CollectionDataPublicationStore Publication { get; } = new();
    public ReportedEvidenceQueue Reports { get; } = new();
    public string? Error
    {
        get; private set;
    }
    public bool Busy => reading || worker is not null || capabilityPreparation is not null;
    public ulong ActiveContentId => identity.ActiveContentId;
    public Task ShutdownTask { get; private set; } = Task.CompletedTask;
    public event Action<string>? Changed;
    public event Action<string>? ChangedV11;
    public event Action<string>? ChangedV12;
    public event Action<string>? Withdrawn;
    public event Action<string>? WithdrawnV11;
    public event Action<string>? WithdrawnV12;

    public SnapshotController(ISnapshotCapturePort capture, AtomicSnapshotStorage storage, FFXIVCollectContract? contract, DiagnosticMeasurements? measurements = null, Func<Func<string>, Task<string>>? prepareCapabilities = null)
    {
        this.prepareCapabilities = prepareCapabilities ?? (work => Task.Run(work));
        this.capture = capture;
        this.storage = storage;
        this.measurements = measurements ?? new();
        publisher = new(storage, contract, this.measurements);
    }
    public void RequestFull()
    {
        if (!disposed && ActiveContentId != 0)
        {
            schedule.Request(CaptureRequest.ManualFull);
        }
    }
    public void RequestPresence()
    {
        if (!disposed && ActiveContentId != 0)
        {
            schedule.Request(CaptureRequest.AutomaticLightweight);
        }
    }
    public void HintSource(string source, ulong contentId, double now)
    {
        if (!disposed && capture.SupportsTargetedCapture)
        {
            prompt.Hint(source, contentId, now);
        }
    }

    public void Update(ulong contentId, string characterName, bool loggedOut, bool zoning, double monotonicNow, DateTimeOffset utcNow)
    {
        if (disposed)
        {
            return;
        }

        var oldId = ActiveContentId;
        var withdrawal = identity.Observe(contentId, loggedOut, zoning, monotonicNow);
        if (withdrawal is not null)
        {
            Invalidate();
            DispatchWithdrawal(oldId, withdrawal, utcNow);
        }
        if (ActiveContentId != oldId && ActiveContentId != 0)
        {
            schedule.Login(monotonicNow);
            state = null;
            restored = false;
        }
        displayName = contentId == ActiveContentId && contentId != 0 ? characterName : displayName;
        prompt.SetCharacter(ActiveContentId, monotonicNow);
        try
        {
            if (worker is { IsCompleted: true })
            {
                var completedTask = worker;
                worker = null;
                WorkerResult result = null!;
                try
                {
                    result = completedTask.GetAwaiter().GetResult();
                }
                catch (Exception) when (workerGeneration != generation) { staleCompletionRejected = true; return; }
                staleCompletionRejected = !SnapshotCompletionPolicy.IsCurrent(result.Generation, result.ContentId, generation, ActiveContentId, scope.IsCancellationRequested);
                if (!staleCompletionRejected)
                {
                    if (result.Targeted && result.Bundle is null && captureError)
                    {
                        Error = null;
                        captureError = false;
                    }
                    if (result.Restoration)
                    {
                        state = result.State;
                        restored = true;
                        Reports.SetScope(ActiveContentId, capture.Catalog!.KnownItemIds, state.ReportedEvidence.Items);
                        if (!result.EvidenceHealthy)
                        {
                            Reports.PersistenceFailed();
                        }

                        Publication.SetCapabilities(CollectionDataLegacyProjection.Capabilities(result.RestoredCapabilities), CollectionDataLegacyProjection.CapabilitiesV11(result.RestoredCapabilities), result.RestoredCapabilities);
                    }
                    if (result.Bundle is { } bundle)
                    {
                        var previous = Publication.Current;
                        state = bundle.Data.State;
                        revision = bundle.Revision;
                        Publication.Accept(bundle);
                        notices.Add(bundle, previous, result.Observed.IsDefault ? ObservedSources(bundle.Data.State.CaptureType) : result.Observed);
                        Publication.SetCapabilities(bundle.CapabilitiesJson, bundle.CapabilitiesJsonV11, bundle.CapabilitiesJsonV12);
                        if (!result.Targeted && !result.Restoration && state.CaptureType != CaptureType.Reported)
                        {
                            schedule.Accepted(state.CaptureType == CaptureType.Full, monotonicNow);
                        }

                        Error = null;
                        captureError = false;
                    }
                }
            }
            if (worker is not null)
            {
                return;
            }

            if (!capture.PrepareCatalog())
            {
                return;
            }

            if (string.IsNullOrEmpty(Publication.GetCapabilities()))
            {
                if (capabilityPreparation is null)
                {
                    operationStarted = monotonicNow;
                    var catalog = capture.Catalog!;
                    var session = Session;
                    var now = utcNow;
                    capabilityPreparation = prepareCapabilities(() => CapabilityDocumentWriter.BuildV12(CollectionDataAssembler.Assemble(catalog, CollectionStateFactory.Empty(0, session, now)), false));
                    return;
                }
                if (!capabilityPreparation.IsCompleted)
                {
                    return;
                }

                var completed = capabilityPreparation;
                capabilityPreparation = null;
                var canonicalCapabilities = completed.GetAwaiter().GetResult();
                Publication.SetCapabilities(CollectionDataLegacyProjection.Capabilities(canonicalCapabilities), CollectionDataLegacyProjection.CapabilitiesV11(canonicalCapabilities), canonicalCapabilities);
            }
            if (ActiveContentId == 0 || contentId == 0)
            {
                return;
            }

            if (!restored)
            {
                operationStarted = monotonicNow;
                StartRestore(utcNow);
                return;
            }
            if (reading)
            {
                if (!capture.Advance())
                {
                    return;
                }

                reading = false;
                var result = capture.TakeResult() ?? throw new InvalidDataException("Capture was invalidated.");
                StartPublication(result, utcNow);
                return;
            }
            schedule.Tick(monotonicNow);
            activeRequest = schedule.Take();
            if (activeRequest != CaptureRequest.None)
            {
                targeted = false;
                operationStarted = monotonicNow;
                generation++;
                persistingReports = false;
                capture.Begin(activeRequest != CaptureRequest.AutomaticLightweight, ActiveContentId, generation, utcNow);
                reading = true;
            }
            else if (Reports.Count != 0)
            {
                operationStarted = monotonicNow;
                StartReports(utcNow);
            }
            else if (capture.SupportsTargetedCapture && prompt.Take(monotonicNow) is { IsEmpty: false } sources)
            {
                targeted = true;
                operationStarted = monotonicNow;
                generation++;
                persistingReports = false;
                capture.BeginTargeted(sources, ActiveContentId, generation, utcNow);
                reading = true;
            }
            var notice = notices.TakeV12();
            if (notice is not null)
            {
                Dispatch(ChangedV12, notice);
                if (CollectionDataLegacyProjection.NoticeV11(notice) is { } v11)
                {
                    Dispatch(ChangedV11, v11);
                }

                if (CollectionDataLegacyProjection.Notice(notice) is { } legacy)
                {
                    Dispatch(Changed, legacy);
                }
            }
        }
        catch (OperationCanceledException) { reading = false; }
        catch (Exception exception)
        {
            var sizeFailure = exception is OutputSizeLimitException;
            measurements.Failure("Controller", "Update", sizeFailure ? "OutputSizeLimitExceeded" : "SynchronizationFailed", exception);
            if (persistingReports)
            {
                Reports.PersistenceFailed();
                if (Publication.Current is { } retained)
                {
                    Publication.SetCapabilities(retained.UnavailableReportsCapabilitiesJson, retained.UnavailableReportsCapabilitiesJsonV11, retained.UnavailableReportsCapabilitiesJsonV12);
                }
            }
            reading = false;
            Error = sizeFailure ? "OutputSizeLimitExceeded: Snapshot exceeds the supported size limit." : "Synchronization failed. Try Sync Now again.";
            captureError = !persistingReports;
            schedule.Failed(activeRequest, monotonicNow, false);
        }
    }

    public DiagnosticStorage StorageDiagnostic => storage.Diagnostic;
    public DiagnosticOperations DiagnosticOperation(double monotonicNow) => new()
    {
        ControllerState = disposed ? "Disposed" : reading ? "Capturing" : worker is not null ? "Working" : capabilityPreparation is not null ? "PreparingCapabilities" : "Idle",
        CaptureType = reading ? (targeted || activeRequest == CaptureRequest.AutomaticLightweight ? "Lightweight" : "Full") : state?.CaptureType.ToString(),
        CaptureGeneration = generation,
        ElapsedMilliseconds = Busy ? Math.Max(0, monotonicNow - operationStarted) * 1000 : 0,
        RetryCount = Math.Min(1000, schedule.FailureCount),
        PendingRequestPriority = schedule.Pending.ToString(),
        StaleCompletionRejected = staleCompletionRejected,
    };
    public ImmutableArray<DiagnosticTask> DiagnosticTasks =>
        [new("CollectionOutput", DiagnosticTaskPolicy.State(worker?.Status)), new("Capabilities", DiagnosticTaskPolicy.State(capabilityPreparation?.Status)), new("CaptureShutdown", DiagnosticTaskPolicy.State(capture.ShutdownTask.Status)), new("ControllerShutdown", DiagnosticTaskPolicy.State(ShutdownTask.Status))];
    public bool CancellationRequested => scope.IsCancellationRequested;
    private void StartRestore(DateTimeOffset now)
    {
        var id = ActiveContentId;
        var gen = generation;
        var session = Session;
        var token = scope.Token;
        var store = storage;
        var catalog = capture.Catalog!;
        var nextRevision = revision + 1;
        workerGeneration = gen;
        persistingReports = false;
        var trustedSources = capture.TrustedSourceKeys;
        worker = Task.Run(async () =>
        {
            CollectionState? restoredState = null;
            var hadCompact = false;
            var evidenceHealthy = true;
            ImmutableArray<ReportedEvidence> evidence = [];
            try
            {
                var compact = await store.ReadAsync(id, false, token).ConfigureAwait(false);
                if (compact is not null)
                {
                    restoredState = CollectionStateReader.Read(compact, id, session, now);
                    hadCompact = true;
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException or ArgumentException) { }
            try
            {
                evidence = CollectionStateReader.ReadReports(await store.ReadAsync(id, true, token).ConfigureAwait(false), id);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException) { evidenceHealthy = false; }
            restoredState ??= CollectionStateFactory.Empty(id, session, now, gen);
            restoredState = RestoredStatePolicy.Reconcile(catalog, restoredState, trustedSources, evidence, evidenceHealthy, now, gen);
            AcceptedSnapshotBundle? bundle = null;
            if (hadCompact)
            {
                var data = CollectionDataAssembler.Assemble(catalog, restoredState);
                var prints = CollectionFingerprint.Sources(data);
                bundle = new(data, nextRevision, CollectionDataSerializer.Compact(data, nextRevision), null, CollectionFingerprint.Combined(prints), prints,
                    PreparedFFXIVCollectExport.Unavailable("Sync to prepare FFXIV Collect output."), 0, null);
                bundle = SnapshotBundlePreparation.Prepare(bundle, null, evidenceHealthy);
            }
            var restoredCapabilities = bundle?.CapabilitiesJsonV12 ?? CapabilityDocumentWriter.BuildV12(CollectionDataAssembler.Assemble(catalog, restoredState), evidenceHealthy);
            return new WorkerResult(gen, id, restoredState, bundle, true, evidenceHealthy, restoredCapabilities);
        }, token);
    }

    private static IEnumerable<string> ObservedSources(CaptureType type) => type switch
    {
        CaptureType.Full => Exportaramus.Features.CollectionData.Contracts.CollectionKeys.Unlocks.Concat(CollectionReadOperation.PhysicalSources),
        CaptureType.Lightweight => CollectionReadOperation.PhysicalSources,
        CaptureType.Reported => ["reportedOwnership"],
        _ => [],
    };
    private void StartPublication(RawCaptureBatch raw, DateTimeOffset now)
    {
        // All normalization is owned by the background worker after the immutable native read completes.
        StartWorker(state!, false, raw: raw, capturedAt: now);
    }
    private void StartReports(DateTimeOffset now)
    {
        var batch = Reports.TakeBatch();
        generation++;
        var candidate = state! with
        {
            CaptureGeneration = generation,
            CaptureType = CaptureType.Reported,
            CapturedAtUtc = now
        };
        StartWorker(candidate, true, batch);
    }
    private void StartWorker(CollectionState candidate, bool reports, ImmutableArray<ReportedEvidence> batch = default, RawCaptureBatch? raw = null, DateTimeOffset capturedAt = default)
    {
        var catalog = capture.Catalog!;
        var gen = generation;
        var id = ActiveContentId;
        var output = publisher;
        var name = displayName;
        var nextRevision = revision + 1;
        var previous = Publication.Current;
        var manual = activeRequest == CaptureRequest.ManualFull;
        var token = scope.Token;
        workerGeneration = gen;
        persistingReports = reports;
        var reportsAvailable = Reports.Available;
        var isTargeted = targeted && raw is not null;
        var full = !isTargeted && activeRequest != CaptureRequest.AutomaticLightweight;
        worker = Task.Run(async () =>
        {
            if (raw is not null)
            {
                var prior = candidate;
                candidate = isTargeted ? TargetedCaptureMerge.Apply(prior, raw, gen, capturedAt) : prior with
                {
                    CaptureGeneration = gen,
                    CapturedAtUtc = capturedAt,
                    CaptureType = full ? CaptureType.Full : CaptureType.Lightweight,
                    Unlocks = full ? raw.Unlocks.ToImmutableDictionary(pair => pair.Key, pair => CollectionStatePolicy.Resolve(prior.Unlocks.GetValueOrDefault(pair.Key), pair.Value, true, false)) : prior.Unlocks,
                    CurrentItemPresence = raw.CurrentItemPresence.ToImmutableDictionary(pair => pair.Key, pair => CollectionStatePolicy.Resolve(prior.CurrentItemPresence.GetValueOrDefault(pair.Key), pair.Value, true, true)),
                    Quests = full ? raw.Quests : prior.Quests,
                };
            }
            if (reports)
            {
                var merged = candidate.ReportedEvidence.Items.ToDictionary(value => (value.ItemId, value.Reporter));
                foreach (var claim in batch)
                {
                    merged[(claim.ItemId, claim.Reporter)] = claim;
                }

                candidate = candidate with
                {
                    ReportedEvidence = candidate.ReportedEvidence with
                    {
                        Items = [.. merged.Values.OrderBy(value => value.ItemId).ThenBy(value => value.Reporter, StringComparer.Ordinal)],
                        Health = candidate.ReportedEvidence.Health with
                        {
                            CaptureGeneration = gen,
                            ObservedAtUtc = candidate.CapturedAtUtc
                        },
                    }
                };
            }
            var observed = !isTargeted ? default : raw!.Unlocks.Keys.Concat(raw.CurrentItemPresence.Keys).Order(StringComparer.Ordinal).ToImmutableArray();
            // Unchanged fallback/hint probes do not churn timestamps, disk writes or notices.
            if (isTargeted && previous is not null && !TargetedCaptureChangePolicy.HasChanges(previous.Data.State, candidate, raw!))
            {
                return new WorkerResult(gen, id, candidate, null, false, reportsAvailable, Targeted: true, Observed: observed);
            }

            var data = measurements.Measure("Normalization", () => CollectionDataAssembler.Assemble(catalog, candidate));
            return new WorkerResult(gen, id, candidate,
                await output.PrepareAndPersistAsync(data, name, nextRevision, previous, manual, reports, token, reportsAvailable).ConfigureAwait(false), false, reportsAvailable, Targeted: isTargeted, Observed: observed);
        }, token);
    }
    private void Invalidate()
    {
        generation++;
        scope.Cancel();
        scope.Dispose();
        scope = new();
        reading = false;
        restored = false;
        state = null;
        capture.ResetSession();
        schedule.Reset();
        Publication.Withdraw();
        notices.Clear();
        Reports.SetScope(0, [], []);
    }
    private void DispatchWithdrawal(ulong id, string reason, DateTimeOffset now)
    {
        var json = Exportaramus.Shared.Serialization.JsonOutput.Text(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("eventType", "SnapshotWithdrawn");
            writer.WriteString("schemaVersion", "1.0");
            writer.WriteString("providerSessionId", Session);
            writer.WriteString("contentId", id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Exportaramus.Shared.Serialization.JsonOutput.Timestamp(writer, "withdrawnAtUtc", now);
            writer.WriteString("reasonCode", reason);
            writer.WriteEndObject();
        });
        Dispatch(Withdrawn, json);
        Dispatch(WithdrawnV11, json.Replace("\"schemaVersion\":\"1.0\"", "\"schemaVersion\":\"1.1\"", StringComparison.Ordinal));
        Dispatch(WithdrawnV12, json.Replace("\"schemaVersion\":\"1.0\"", "\"schemaVersion\":\"1.2\"", StringComparison.Ordinal));
    }
    private static void Dispatch(Action<string>? handlers, string json)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (Action<string> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(json);
            }
            catch (Exception) { /* A consumer cannot roll back publication. */ }
        }
    }
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Reports.Stop();
        var departing = ActiveContentId;
        Invalidate();
        if (departing != 0)
        {
            DispatchWithdrawal(departing, "ProviderStopping", DateTimeOffset.UtcNow);
        }

        capture.Dispose();
        var owned = worker;
        worker = null;
        ShutdownTask = ObserveAsync(Task.WhenAll((Task?)owned ?? Task.CompletedTask, (Task?)capabilityPreparation ?? Task.CompletedTask, capture.ShutdownTask), scope);
    }
    private static async Task ObserveAsync(Task? task, CancellationTokenSource token)
    {
        try
        {
            if (task is not null)
            {
                await task.ConfigureAwait(false);
            }
        }
        catch (Exception) { }
        finally { token.Dispose(); }
    }
    private sealed record WorkerResult(long Generation, ulong ContentId, CollectionState State, AcceptedSnapshotBundle? Bundle, bool Restoration, bool EvidenceHealthy = true, string RestoredCapabilities = "", bool Targeted = false, ImmutableArray<string> Observed = default);
}
