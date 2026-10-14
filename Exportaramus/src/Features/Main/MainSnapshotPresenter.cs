using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Diagnostics;
using Exportaramus.Features.GuidedExport;

namespace Exportaramus.Features.Main;

/// <summary>Projects bounded snapshot metadata. Collection item arrays and payload strings are never scanned here.</summary>
public static class MainSnapshotPresenter
{
    private static bool IsComplete(AcceptedSnapshotBundle bundle)
    {
        // Capture success and reader coverage are separate. Omitted/closed windows do not undo a successful read.
        if (bundle.Data.State.CaptureType == CaptureType.Restored || bundle.Data.Collections.Count == 0) return false;
        foreach (var definition in MainPresenter.Collections)
        {
            if (!bundle.Data.SourceHealth.TryGetValue(definition.Key, out var health)) continue;
            if (health.ReasonCode is ReasonCode.CaptureFailed or ReasonCode.MappingValidationFailed or ReasonCode.QuantityOverflow or ReasonCode.OutputSizeLimitExceeded)
                return false;
            if (health.CapabilityState == CapabilityState.UnavailablePendingValidation || !health.IsUsable) continue;
            if (!bundle.Data.Collections.ContainsKey(definition.Key) || health.CapabilityState == CapabilityState.Degraded) return false;
            if (health.SourceStatus == SourceStatus.Stale && !RetainedClosedWindow(health)) return false;
        }
        return true;
    }

    private static bool RetainedClosedWindow(CollectionSourceStatus health) =>
        health.DataType == DataType.CurrentItemPresence && health.SourceStatus == SourceStatus.Stale &&
        health.ProvenanceType == ProvenanceType.Observed && health.ReasonCode == ReasonCode.WindowNotReady;

    public static MainViewState Prepare(AcceptedSnapshotBundle? bundle, bool hasCharacter, string? synchronizationError,
        bool busy, bool reduceMotion, GuidedView guided, DateTimeOffset now, bool ipcAvailable)
    {
        var status = !hasCharacter ? MainStatus.NoCharacter : synchronizationError is not null ? MainStatus.Error : busy ? MainStatus.Synchronizing :
            bundle is null ? MainStatus.NoSnapshot : IsComplete(bundle) ? MainStatus.Current : MainStatus.Partial;
        var rows = ImmutableDictionary.CreateBuilder<string, CollectionRowObservation>();
        if (bundle is not null)
        {
            foreach (var definition in MainPresenter.Collections)
            {
                if (!bundle.Data.Collections.TryGetValue(definition.Key, out var collection))
                {
                    bundle.Data.SourceHealth.TryGetValue(definition.Key, out var health);
                    var (label, detail) = MissingStatus(definition.Key, health);
                    var partial = bundle.ConfirmedPartialCounts.TryGetValue(definition.Key, out var confirmed);
                    rows.Add(definition.Key, new(partial ? confirmed : null, false, null, RowEvidenceKind.Derived,
                        label, partial ? "At least " + confirmed + " outfits confirmed from observed storage. Other storage is missing; this is not a complete total and is omitted from JSON." : detail, partial));
                    continue;
                }
                rows.Add(definition.Key, new(bundle.CollectionOwnedCounts.TryGetValue(definition.Key, out var count) ? count : null,
                    collection.Health.SourceStatus == SourceStatus.Stale && !RetainedClosedWindow(collection.Health),
                    bundle.CollectionDeltas.TryGetValue(definition.Key, out var delta) ? delta : null,
                    collection.OwnershipKind switch { DataType.PermanentUnlock => RowEvidenceKind.PermanentUnlock, DataType.CurrentItemPresence => RowEvidenceKind.CurrentItemPresence, _ => RowEvidenceKind.Derived }, Detail: collection.Health.SourceStatus == SourceStatus.Stale
                        ? RetainedClosedWindow(collection.Health) ? "Captured " + MainPresenter.RelativeTime(collection.Health.ObservedAtUtc, now).ToLowerInvariant() + "; window now closed. Last observed count retained." : "Last-known count retained. " + collection.Health.Reason
                        : "Captured " + MainPresenter.RelativeTime(collection.Health.ObservedAtUtc, now).ToLowerInvariant() + ". Complete read; zero means no owned entries."));
            }
        }
        var collectorError = bundle is not null && bundle.Data.SourceHealth.Values.Any(x => x.ReasonCode == ReasonCode.CaptureFailed || x.ReasonCode == ReasonCode.MappingValidationFailed || x.ReasonCode == ReasonCode.QuantityOverflow);
        var pendingValidation = bundle is not null && bundle.Data.SourceHealth.Values.Any(x => x.CapabilityState == CapabilityState.UnavailablePendingValidation);
        var headline = status switch
        {
            MainStatus.NoCharacter => "Waiting for Character",
            MainStatus.NoSnapshot => "No Snapshot Available",
            MainStatus.Synchronizing => "Synchronizing",
            MainStatus.Current => "Capture Complete",
            MainStatus.Error => "Refresh Failed",
            _ => "Some Data Unavailable",
        };
        var healthTone = status switch { MainStatus.Current => UiTone.Success, MainStatus.Error => UiTone.Error, MainStatus.Partial => UiTone.Warning, MainStatus.Synchronizing => UiTone.Working, _ => UiTone.Muted };
        var observed = bundle is null ? "Not observed" : bundle.Data.State.CaptureType == CaptureType.Restored ? "Restored snapshot" : now - bundle.Data.State.CapturedAtUtc < TimeSpan.FromMinutes(1) ? "Observed just now" : "Snapshot retained";
        var observationsTone = bundle is null ? UiTone.Muted : bundle.Data.State.CaptureType == CaptureType.Restored ? UiTone.Warning : UiTone.Success;
        return MainPresenter.Prepare(new(bundle?.Revision ?? 0, status, hasCharacter, bundle is not null, reduceMotion, bundle?.Export.CanCopy == true,
            bundle?.HistoryCount ?? 0, bundle?.Data.State.CapturedAtUtc, bundle?.LatestHistoryUtc, synchronizationError ?? bundle?.Export.Error ?? "",
            headline, healthTone, [new(observed, observationsTone), new(collectorError ? "Collector errors" : pendingValidation ? "Reader unavailable" : "No collector errors",
                collectorError ? UiTone.Error : pendingValidation ? UiTone.Warning : UiTone.Success), new(ipcAvailable ? "IPC available" : "IPC unavailable", ipcAvailable ? UiTone.Success : UiTone.Warning)], rows.ToImmutable(), guided), now) with
        { HealthTooltip = PrepareHealthTooltip(bundle, observed, collectorError, ipcAvailable) };
    }

    private static readonly ImmutableDictionary<string, string> SourceLabels = MainPresenter.Collections
        .Select(source => new KeyValuePair<string, string>(source.Key, source.Label))
        .Concat(new Dictionary<string, string>
        {
            ["facewear"] = "Facewear",
            ["beasts"] = "Beasts",
            ["inventory"] = "Inventory",
            ["armoryChest"] = "Armoury Chest",
            ["chocoboSaddlebag"] = "Chocobo Saddlebag",
            ["premiumSaddlebag"] = "Premium Saddlebag",
            ["retainers"] = "Retainers",
            ["armoire"] = "Armoire",
            ["glamourDresser"] = "Glamour Dresser",
        }).ToImmutableDictionary(StringComparer.Ordinal);

    private static string PrepareHealthTooltip(AcceptedSnapshotBundle? bundle, string observed, bool collectorError, bool ipcAvailable)
    {
        var lines = new List<string> { observed, collectorError ? "Collector errors" : "No collector errors" };
        if (bundle is not null)
            foreach (var source in bundle.Data.SourceHealth.OrderBy(source => source.Key, StringComparer.Ordinal))
                if (source.Value.CapabilityState == CapabilityState.UnavailablePendingValidation)
                    lines.Add("Reader Unavailable - " + SourceLabels.GetValueOrDefault(source.Key, source.Key));
        lines.Add(ipcAvailable ? "IPC Available" : "IPC Unavailable");
        return string.Join("\n", lines);
    }
    private static (string Label, string Detail) MissingStatus(string key, CollectionSourceStatus? health) => health?.ReasonCode switch
    {
        ReasonCode.NotLoadedThisSession => ("Waiting", "Open " + (key == "achievements" ? "Achievements" : "the collection window") + " and wait for it to finish loading."),
        ReasonCode.WindowNotReady => ("Waiting", "Open " + (key == "glamour_dresser" ? "the Glamour Dresser" : key == "armoires" ? "the Armoire" : "the required window") + " and keep it open until capture completes."),
        ReasonCode.IncompleteContainerSet or ReasonCode.IncompleteRetainerSet => ("Incomplete", "Required storage has not been completely observed. Missing data is not an empty collection."),
        ReasonCode.PatchValidationRequired or ReasonCode.GameDataVersionUnsupported => ("Unavailable", "This reader is not enabled for the current game build."),
        ReasonCode.MappingValidationFailed or ReasonCode.CaptureFailed or ReasonCode.QuantityOverflow or ReasonCode.OutputSizeLimitExceeded => ("Error", "Capture failed: " + health.ReasonCode + ". The collection has not been treated as empty."),
        _ => ("Waiting", "Not captured yet. Use Sync Now or open the required collection window."),
    };

    public static DiagnosticReportInput DiagnosticInput(AcceptedSnapshotBundle? bundle, string pluginVersion, string runtimeVersion,
        Guid session, bool busy, bool reduceMotion, int reportQueueDepth, DiagnosticCounters reportCounters,
        DiagnosticCollect contractEvidence, DateTimeOffset now)
    {
        var sources = bundle is null ? [] : bundle.Data.SourceHealth.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new DiagnosticSource(pair.Key,
            pair.Value.CapabilityState.ToString(), pair.Value.SourceStatus.ToString(), pair.Value.Completeness.ToString(), pair.Value.ProvenanceType.ToString(),
            pair.Value.ObservedAtUtc is { } observed ? Math.Max(0, (now - observed).TotalSeconds) : null, 0, pair.Value.ReasonCode.ToString())).ToImmutableArray();
        var omissions = bundle is null ? [] : bundle.Data.SourceHealth.Where(pair => !pair.Value.IsUsable).OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new DiagnosticOmission(pair.Key, pair.Value.ReasonCode.ToString())).ToImmutableArray();
        return new(new() { PluginVersion = pluginVersion, AssemblyInformationalVersion = pluginVersion },
            new() { DotnetRuntimeVersion = runtimeVersion, GameDataVersion = bundle?.Data.Catalog.GameDataVersion ?? "Unavailable", FfxivClientVersion = bundle?.Data.Catalog.GameDataVersion ?? "Unavailable", ClientLanguage = bundle?.Data.Catalog.ClientLanguage ?? "English" },
            new() { State = "Running", ProviderSessionId = session, Revision = bundle?.Revision ?? 0, Registrations = new(true, true, true, true, true, true, true, true) },
            new() { ControllerState = busy ? "Capturing" : "Idle", CaptureType = bundle?.Data.State.CaptureType.ToString(), CaptureGeneration = bundle?.Data.State.CaptureGeneration ?? 0 },
            new()
            {
                AcceptedCaptureGeneration = bundle?.Data.State.CaptureGeneration ?? 0,
                CatalogCache = new(null, bundle is null ? "Empty" : "Ready", 0),
                Fingerprints = bundle?.SourceFingerprints.ToImmutableDictionary(x => "source." + x.Key, x => (string?)x.Value, StringComparer.Ordinal) ?? ImmutableDictionary<string, string?>.Empty,
                FfxivCollect = contractEvidence with { IncludedKeyCount = bundle?.Export.IncludedKeys.Length ?? 0, OmittedKeyCount = bundle?.Export.OmittedKeys.Length ?? 16 },
                Serializers = new(bundle is null ? "NotBuilt" : "Ready", bundle?.RichJson is null ? "NotBuilt" : "Ready", bundle?.Export.CanCopy == true ? "Ready" : "Unavailable"),
                OutputBytes = new(bundle?.CompactBytes ?? 0, bundle?.RichBytes ?? 0, bundle?.ExportBytes ?? 0),
                Omissions = omissions
            },
            sources, new() { HistoryFileCount = bundle?.HistoryCount ?? 0 },
            new()
            {
                GatesRegistered = new(true, true, true, true, true),
                ReportQueue = new(reportQueueDepth, 256),
                ReportCounters = reportCounters,
                PayloadBytes = new(bundle?.CapabilitiesBytes ?? 0, bundle?.CompactBytes ?? 0, 0)
            },
            new() { ReduceMotion = reduceMotion, ViewStateRevision = bundle?.Revision ?? 0 }, ["storage", "ui"]);
    }
}
