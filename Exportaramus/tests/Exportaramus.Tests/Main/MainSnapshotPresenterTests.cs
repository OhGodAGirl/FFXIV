using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Diagnostics;
using Exportaramus.Features.GuidedExport;
using Exportaramus.Features.Main;
using Exportaramus.Tests.Snapshots;

namespace Exportaramus.Tests.Main;

public sealed class MainSnapshotPresenterTests
{
    [Fact]
    public void DiagnosticsSerializesActualSourceFingerprintsWithoutExposingInventory()
    {
        var bundle = Bundle();
        bundle = bundle with { SourceFingerprints = CollectionFingerprint.Sources(bundle.Data) };
        var input = MainSnapshotPresenter.DiagnosticInput(bundle, "0.2.4.2", "10.0.0", Guid.Empty, false, false, 0, new(0, 0, 0), new(), SnapshotFixture.Now);
        var prepared = DiagnosticReportSerializer.Prepare(DiagnosticReportPreparer.Prepare(input, SnapshotFixture.Now));
        DiagnosticReportSerializer.ValidateWireJson(prepared.Json);
        Assert.Equal(bundle.SourceFingerprints["inventory"], prepared.Model.CollectionPipeline.Fingerprints["source.inventory"]);
        Assert.DoesNotContain("\"inventory\":", prepared.Json);
    }

    [Theory]
    [InlineData(16, 0)]
    [InlineData(0, 16)]
    public void DiagnosticsAcceptsBeastsExportCategoryCounts(int included, int omitted)
    {
        var report = new DiagnosticReport
        {
            GeneratedAtUtc = SnapshotFixture.Now.UtcDateTime,
            CollectionPipeline = new() { FfxivCollect = new() { IncludedKeyCount = included, OmittedKeyCount = omitted } },
        };
        DiagnosticReportSerializer.ValidateWireJson(DiagnosticReportSerializer.Prepare(report).Json);
    }
    [Theory]
    [InlineData(17, 0)]
    [InlineData(0, 17)]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void DiagnosticsRejectsExportCountsOutsideSupportedDomain(int included, int omitted)
    {
        var report = new DiagnosticReport
        {
            GeneratedAtUtc = SnapshotFixture.Now.UtcDateTime,
            CollectionPipeline = new() { FfxivCollect = new() { IncludedKeyCount = included, OmittedKeyCount = omitted } },
        };
        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.Prepare(report));
    }
    private static GuidedView Guided => new GuidedExportController().View;
    private static AcceptedSnapshotBundle Bundle()
    {
        var data = SnapshotFixture.Create();
        return new(data, 12, "{compact}", "{rich}", "fingerprint", ImmutableDictionary<string, string>.Empty,
            new("{export}", ["bardings"], ["armoires"], null), 5, SnapshotFixture.Now)
        {
            CollectionOwnedCounts = data.Collections.ToImmutableDictionary(x => x.Key, _ => 17),
            CollectionDeltas = ImmutableDictionary<string, int>.Empty.Add("mounts", 2).Add("armoires", -3).Add("outfits", -1),
            CompactBytes = 1001,
            RichBytes = 2002,
            ExportBytes = 3003,
            CapabilitiesBytes = 444,
        };
    }
    private static MainViewState View(AcceptedSnapshotBundle? bundle, bool hasCharacter = true, string? error = null, bool busy = false, bool ipc = true) =>
        MainSnapshotPresenter.Prepare(bundle, hasCharacter, error, busy, false, Guided, SnapshotFixture.Now, ipc);

    [Fact]
    public void HealthTooltipPreservesObservationAndIpcStates()
    {
        Assert.Equal("Not observed\nNo collector errors\nIPC Unavailable", View(null, ipc: false).HealthTooltip);
        Assert.Equal("Observed just now\nNo collector errors\nIPC Available", View(Bundle()).HealthTooltip);
        var bundle = Bundle();
        var restored = bundle with { Data = bundle.Data with { State = bundle.Data.State with { CaptureType = CaptureType.Restored } } };
        Assert.StartsWith("Restored snapshot\n", View(restored).HealthTooltip);
        Assert.StartsWith("Snapshot retained\n", MainSnapshotPresenter.Prepare(bundle, true, null, false, false, Guided, SnapshotFixture.Now.AddMinutes(1), true).HealthTooltip);
        Assert.Equal("", new MainViewState(0, MainStatus.NoSnapshot, "", UiTone.Muted, "", false, false, false, false, 0, "", "", UiTone.Muted, [], [], "", Guided).HealthTooltip);
    }

    [Fact]
    public void HealthTooltipNamesAllUnavailableReadersWithoutTruncationAndKeepsCollectorErrors()
    {
        var bundle = Bundle();
        var names = new Dictionary<string, string>
        {
            ["field_records"] = "Field Records",
            ["facewear"] = "Facewear",
            ["beasts"] = "Beasts",
            ["inventory"] = "Inventory",
            ["armoryChest"] = "Armoury Chest",
            ["chocoboSaddlebag"] = "Chocobo Saddlebag",
            ["premiumSaddlebag"] = "Premium Saddlebag",
            ["retainers"] = "Retainers",
            ["armoire"] = "Armoire",
            ["glamourDresser"] = "Glamour Dresser",
            ["future_source"] = "future_source",
        };
        var health = bundle.Data.SourceHealth;
        foreach (var key in names.Keys)
            health = health.SetItem(key, bundle.Data.SourceHealth["mounts"] with { CapabilityState = CapabilityState.UnavailablePendingValidation });
        health = health.SetItem("mounts", health["mounts"] with { ReasonCode = ReasonCode.CaptureFailed });
        var view = View(bundle with { Data = bundle.Data with { SourceHealth = health } });
        var expected = new[] { "Observed just now", "Collector errors" }
            .Concat(names.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => "Reader Unavailable - " + pair.Value))
            .Append("IPC Available");
        Assert.Equal(string.Join("\n", expected), view.HealthTooltip);
        Assert.Equal(MainStatus.Partial, view.Status);
    }
    [Fact]
    public void MissingHealthShowsWaitingRatherThanImplyingAnEmptyCollection()
    {
        var bundle = Bundle();
        bundle = bundle with { Data = bundle.Data with { Collections = bundle.Data.Collections.Remove("mounts"), SourceHealth = bundle.Data.SourceHealth.Remove("mounts") } };
        Assert.Equal("Waiting", View(bundle).Rows.Single(row => row.Key == "mounts").Count);
    }

    [Theory]
    [InlineData("achievements", ReasonCode.NotLoadedThisSession, "Waiting", "Achievements")]
    [InlineData("glamour_dresser", ReasonCode.WindowNotReady, "Waiting", "Glamour Dresser")]
    [InlineData("outfits", ReasonCode.IncompleteContainerSet, "Incomplete", "storage")]
    [InlineData("glamour_dresser", ReasonCode.CaptureFailed, "Error", "CaptureFailed")]
    public void OmittedCollectionsExplainTheirState(string key, ReasonCode reason, string label, string detail)
    {
        var bundle = Bundle();
        var health = SnapshotFixture.Health(DataType.Derived, SourceStatus.Unavailable) with { ReasonCode = reason };
        bundle = bundle with { Data = bundle.Data with { Collections = bundle.Data.Collections.Remove(key), SourceHealth = bundle.Data.SourceHealth.SetItem(key, health) } };
        var row = View(bundle).Rows.Single(x => x.Key == key);
        Assert.Equal(label, row.Count);
        Assert.Contains(detail, row.Detail);
    }

    [Theory]
    [InlineData("mounts", ReasonCode.NotLoadedThisSession, "Waiting", "collection window")]
    [InlineData("armoires", ReasonCode.WindowNotReady, "Waiting", "Armoire")]
    [InlineData("mounts", ReasonCode.WindowNotReady, "Waiting", "required window")]
    [InlineData("mounts", ReasonCode.IncompleteRetainerSet, "Incomplete", "storage")]
    [InlineData("mounts", ReasonCode.GameDataVersionUnsupported, "Unavailable", "game build")]
    [InlineData("mounts", ReasonCode.MappingValidationFailed, "Error", "MappingValidationFailed")]
    [InlineData("mounts", ReasonCode.QuantityOverflow, "Error", "QuantityOverflow")]
    [InlineData("mounts", ReasonCode.OutputSizeLimitExceeded, "Error", "OutputSizeLimitExceeded")]
    public void AdditionalMissingReasonsRemainExplicit(string key, ReasonCode reason, string label, string detail) =>
        OmittedCollectionsExplainTheirState(key, reason, label, detail);

    [Fact]
    public void OutputLimitFailureIsPartialAndRetainerUiStateIsPreserved()
    {
        var bundle = Bundle();
        bundle = bundle with
        {
            Data = bundle.Data with
            {
                SourceHealth = bundle.Data.SourceHealth.SetItem("mounts",
            bundle.Data.SourceHealth["mounts"] with { ReasonCode = ReasonCode.OutputSizeLimitExceeded })
            }
        };
        var view = View(bundle) with { RetainersActive = true };
        Assert.Equal(MainStatus.Partial, view.Status);
        Assert.True(view.RetainersActive);
    }

    [Fact]
    public void CompletedCaptureIsCurrentWithUnavailableReadersAndClosedDresser()
    {
        var bundle = Bundle();
        var closed = bundle.Data.SourceHealth["glamour_dresser"] with { SourceStatus = SourceStatus.Stale, ReasonCode = ReasonCode.WindowNotReady };
        var unavailable = bundle.Data.SourceHealth["field_records"] with { SourceStatus = SourceStatus.Unavailable, CapabilityState = CapabilityState.UnavailablePendingValidation, ReasonCode = ReasonCode.PatchValidationRequired };
        bundle = bundle with
        {
            Data = bundle.Data with
            {
                SourceHealth = bundle.Data.SourceHealth.SetItem("glamour_dresser", closed).SetItem("field_records", unavailable),
                Collections = bundle.Data.Collections.Remove("field_records").SetItem("glamour_dresser", bundle.Data.Collections["glamour_dresser"] with { Health = closed })
            }
        };
        var view = View(bundle);
        Assert.Equal(MainStatus.Current, view.Status);
        Assert.Equal("Capture Complete", view.HealthHeadline);
        Assert.Equal("Unavailable", view.Rows.Single(x => x.Key == "field_records").Count);
        Assert.Empty(view.Rows.Single(x => x.Key == "glamour_dresser").Marker);
        Assert.Contains("window now closed", view.Rows.Single(x => x.Key == "glamour_dresser").Detail);
    }

    [Fact]
    public void ZeroOwnedEntriesDoNotMakeACompleteReadPartial()
    {
        var bundle = Bundle();
        bundle = bundle with { CollectionOwnedCounts = bundle.CollectionOwnedCounts.ToImmutableDictionary(x => x.Key, _ => 0) };
        var view = View(bundle);
        Assert.Equal(MainStatus.Current, view.Status);
        Assert.All(view.Rows, row => Assert.Equal("0", row.Count));
    }

    [Fact]
    public void PartialOutfitCountCannotBeMistakenForCompleteOrEmpty()
    {
        var bundle = Bundle();
        bundle = bundle with
        {
            Data = bundle.Data with { Collections = bundle.Data.Collections.Remove("outfits") },
            ConfirmedPartialCounts = ImmutableDictionary<string, int>.Empty.Add("outfits", 3)
        };
        var row = View(bundle).Rows.Single(x => x.Key == "outfits");
        Assert.Equal("3+", row.Count);
        Assert.Contains("omitted from JSON", row.Detail);
        Assert.Empty(row.Delta);
    }

    [Fact]
    public void CachedCountsAndDeltasAreUsedWithoutReadingCollectionItems()
    {
        var bundle = Bundle();
        var view = View(bundle);
        Assert.Equal(MainStatus.Current, view.Status); Assert.Equal("Capture Complete", view.HealthHeadline);
        Assert.Equal(12, view.Revision); Assert.Equal(5, view.HistoryCount); Assert.Equal("", view.Message);
        Assert.All(view.Rows, row => Assert.Equal("17", row.Count));
        Assert.Equal("+2", view.Rows.Single(x => x.Key == "mounts").Delta);
        Assert.Equal(UiTone.Warning, view.Rows.Single(x => x.Key == "armoires").DeltaTone);
        Assert.Equal(UiTone.Error, view.Rows.Single(x => x.Key == "outfits").DeltaTone);
        Assert.Equal("Observed just now", view.HealthFacts[0].Text);
        Assert.Equal("No collector errors", view.HealthFacts[1].Text);
        Assert.Equal(UiTone.Success, view.HealthFacts[0].Tone); Assert.Equal(UiTone.Success, view.HealthFacts[1].Tone);
        Assert.Equal("IPC available", view.HealthFacts[2].Text);
        Assert.Equal(UiTone.Success, view.HealthFacts[2].Tone);
        var omittedCount = View(bundle with { CollectionOwnedCounts = ImmutableDictionary<string, int>.Empty });
        Assert.All(omittedCount.Rows, row => Assert.Equal("—", row.Count));
    }
    [Fact]
    public void MainStatesPreserveThePriorSummaryAndDisplayUsefulHealthText()
    {
        Assert.Equal(MainStatus.NoCharacter, View(null, false).Status);
        Assert.Equal("Waiting for Character", View(null, false).HealthHeadline);
        Assert.Equal(MainStatus.NoSnapshot, View(null).Status);
        Assert.Equal("No Snapshot Available", View(null).HealthHeadline);
        Assert.Equal("Not observed", View(null).HealthFacts[0].Text);
        var bundle = Bundle();
        var busy = View(bundle, busy: true); Assert.Equal(MainStatus.Synchronizing, busy.Status); Assert.Equal("Synchronizing", busy.HealthHeadline); Assert.True(busy.CanCopy);
        var error = View(bundle, error: "Refresh failed. Try again."); Assert.Equal(MainStatus.Error, error.Status); Assert.Equal("Refresh Failed", error.HealthHeadline); Assert.Equal(UiTone.Error, error.HealthTone); Assert.Equal(23, error.Rows.Length);
        Assert.Equal("Refresh failed. Try again.", error.Message);
        Assert.Equal("IPC unavailable", View(bundle, ipc: false).HealthFacts[2].Text);
        Assert.Equal(UiTone.Warning, View(bundle, ipc: false).HealthFacts[2].Tone);
        var fewer = bundle with { Data = bundle.Data with { Collections = bundle.Data.Collections.Remove("mounts") } };
        Assert.Equal(MainStatus.Partial, View(fewer).Status); Assert.Equal("Some Data Unavailable", View(fewer).HealthHeadline);
        var outputError = View(bundle with { Export = PreparedFFXIVCollectExport.Unavailable("No eligible collections.") });
        Assert.False(outputError.CanCopy); Assert.Equal("No eligible collections.", outputError.Message);
    }
    [Theory]
    [InlineData(ReasonCode.CaptureFailed)]
    [InlineData(ReasonCode.MappingValidationFailed)]
    [InlineData(ReasonCode.QuantityOverflow)]
    public void CollectorFailuresHaveAnErrorFact(ReasonCode reason)
    {
        var bundle = Bundle(); var health = bundle.Data.SourceHealth["mounts"] with { ReasonCode = reason };
        var view = View(bundle with { Data = bundle.Data with { SourceHealth = bundle.Data.SourceHealth.SetItem("mounts", health) } });
        Assert.Equal("Collector errors", view.HealthFacts[1].Text); Assert.Equal(UiTone.Error, view.HealthFacts[1].Tone);
    }
    [Fact]
    public void StaleDegradedAndPendingValidationRemainExplicit()
    {
        var bundle = Bundle(); var health = bundle.Data.SourceHealth["mounts"];
        foreach (var changed in new[] { health with { SourceStatus = SourceStatus.Stale }, health with { CapabilityState = CapabilityState.Degraded } })
            Assert.Equal(MainStatus.Partial, View(bundle with { Data = bundle.Data with { SourceHealth = bundle.Data.SourceHealth.SetItem("mounts", changed) } }).Status);
        var pending = View(bundle with { Data = bundle.Data with { SourceHealth = bundle.Data.SourceHealth.SetItem("mounts", health with { CapabilityState = CapabilityState.UnavailablePendingValidation }) } });
        Assert.Equal("Reader unavailable", pending.HealthFacts[1].Text); Assert.Equal(UiTone.Warning, pending.HealthFacts[1].Tone);
        var stale = View(bundle with { Data = bundle.Data with { Collections = bundle.Data.Collections.SetItem("mounts", bundle.Data.Collections["mounts"] with { Health = health with { SourceStatus = SourceStatus.Stale } }), State = bundle.Data.State with { CaptureType = CaptureType.Restored } } });
        Assert.Equal("!", stale.Rows.Single(x => x.Key == "mounts").Marker); Assert.Equal("Restored snapshot", stale.HealthFacts[0].Text); Assert.Equal(UiTone.Warning, stale.HealthFacts[0].Tone);
        var older = MainSnapshotPresenter.Prepare(bundle, true, null, false, true, Guided, SnapshotFixture.Now.AddMinutes(1), true);
        Assert.Equal("Snapshot retained", older.HealthFacts[0].Text); Assert.True(older.ReduceMotion);
    }
    [Fact]
    public void DiagnosticProjectionUsesCachedPayloadByteCountsAndBoundedMetadataOnly()
    {
        var bundle = Bundle(); var evidence = new DiagnosticCollect(); var counters = new DiagnosticCounters(2, 3, 4);
        var input = MainSnapshotPresenter.DiagnosticInput(bundle, "0.1.0.0", "10.0.0", bundle.Data.State.ProviderSessionId, true, true, 7, counters, evidence, SnapshotFixture.Now);
        var report = DiagnosticReportPreparer.Prepare(input, SnapshotFixture.Now);
        Assert.Equal("Partial", report.ReportStatus); Assert.Equal(new[] { "storage", "ui" }, report.IncompleteSections);
        Assert.Equal("Capturing", report.Operations.ControllerState); Assert.Equal(33, report.SourceHealth.Length); Assert.All(report.SourceHealth, x => Assert.Equal(0, x.ObservationAgeSeconds));
        Assert.Contains(report.SourceHealth, source => source.SourceKey == "beasts");
        Assert.Equal(bundle.Data.State.CaptureGeneration, report.Operations.CaptureGeneration);
        Assert.Equal(bundle.Data.State.CaptureGeneration, report.CollectionPipeline.AcceptedCaptureGeneration);
        Assert.Equal(bundle.Data.Catalog.ClientLanguage, report.Runtime.ClientLanguage);
        Assert.Equal(bundle.Data.Catalog.GameDataVersion, report.Runtime.GameDataVersion);
        Assert.Equal("Ready", report.CollectionPipeline.CatalogCache.Status);
        Assert.Equal(new DiagnosticSerializers("Ready", "Ready", "Ready"), report.CollectionPipeline.Serializers);
        Assert.Equal(1, report.CollectionPipeline.FfxivCollect.IncludedKeyCount); Assert.Equal(1, report.CollectionPipeline.FfxivCollect.OmittedKeyCount);
        Assert.Equal(new DiagnosticOutputBytes(1001, 2002, 3003), report.CollectionPipeline.OutputBytes);
        Assert.Equal(new DiagnosticPayloadBytes(444, 1001, 0), report.Ipc.PayloadBytes);
        Assert.Equal(counters, report.Ipc.ReportCounters); Assert.Equal(new DiagnosticQueue(7, 256), report.Ipc.ReportQueue);
        Assert.Equal(12, report.Lifecycle.Revision); Assert.Equal(5, report.Storage.HistoryFileCount); Assert.True(report.Ui.ReduceMotion);
        Assert.Equal(12, report.Ui.ViewStateRevision);
        Assert.Equal("Ready", DiagnosticReportPreparer.Prepare(input with { IncompleteSections = [] }, SnapshotFixture.Now).ReportStatus);
        DiagnosticReportSerializer.Prepare(report);
    }
    [Fact]
    public void DiagnosticSourceAndOmissionOrderingIsOrdinalAndCatalogLanguageIsNotAssumed()
    {
        var bundle = Bundle();
        var health = bundle.Data.SourceHealth.ToImmutableDictionary(pair => pair.Key, pair => pair.Value with { SourceStatus = SourceStatus.Unavailable });
        bundle = bundle with { Data = bundle.Data with { Catalog = bundle.Data.Catalog with { ClientLanguage = "French" }, SourceHealth = health } };

        var input = MainSnapshotPresenter.DiagnosticInput(bundle, "0.1.0.0", "10.0.0", Guid.Empty, false, false, 0, new(0, 0, 0), new(), SnapshotFixture.Now);

        Assert.Equal("French", input.Runtime.ClientLanguage);
        Assert.Equal(health.Keys.Order(StringComparer.Ordinal), input.Sources.Select(source => source.SourceKey));
        Assert.Equal(health.Keys.Order(StringComparer.Ordinal), input.Pipeline.Omissions.Select(source => source.SourceKey));
    }
    [Fact]
    public void DiagnosticProjectionCoversNoSnapshotOmissionsAndUnknownObservationTimes()
    {
        var empty = MainSnapshotPresenter.DiagnosticInput(null, "0.1.0.0", "10.0.0", Guid.Empty, false, false, 0, new(0, 0, 0), new(), SnapshotFixture.Now);
        Assert.Equal("Unavailable", empty.Runtime.GameDataVersion); Assert.Equal("English", empty.Runtime.ClientLanguage);
        Assert.Equal("Idle", empty.Operations.ControllerState); Assert.Null(empty.Operations.CaptureType); Assert.Empty(empty.Sources);
        Assert.Equal("NotBuilt", empty.Pipeline.Serializers.Compact); Assert.Equal("NotBuilt", empty.Pipeline.Serializers.Compendium); Assert.Equal("Unavailable", empty.Pipeline.Serializers.FfxivCollect);
        Assert.Equal(new DiagnosticOutputBytes(0, 0, 0), empty.Pipeline.OutputBytes);
        Assert.Equal(0, empty.Pipeline.FfxivCollect.IncludedKeyCount); Assert.Equal(16, empty.Pipeline.FfxivCollect.OmittedKeyCount);
        var bundle = Bundle(); var health = bundle.Data.SourceHealth["mounts"] with { SourceStatus = SourceStatus.Unavailable, ObservedAtUtc = null, Completeness = Completeness.Unknown, ProvenanceType = ProvenanceType.None, ReasonCode = ReasonCode.PatchValidationRequired };
        bundle = bundle with { Data = bundle.Data with { SourceHealth = bundle.Data.SourceHealth.SetItem("mounts", health) }, RichJson = null, Export = PreparedFFXIVCollectExport.Unavailable("No eligible collections."), SourceFingerprints = ImmutableDictionary<string, string>.Empty.Add("mounts", new string('a', 64)) };
        var projected = MainSnapshotPresenter.DiagnosticInput(bundle, "0.1.0.0", "10.0.0", Guid.Empty, false, false, 0, new(0, 0, 0), new(), SnapshotFixture.Now.AddSeconds(-5));
        Assert.Null(projected.Sources.Single(x => x.SourceKey == "mounts").ObservationAgeSeconds); Assert.All(projected.Sources.Where(x => x.SourceKey != "mounts"), x => Assert.Equal(0, x.ObservationAgeSeconds));
        Assert.Equal(new DiagnosticOmission("mounts", "PatchValidationRequired"), Assert.Single(projected.Pipeline.Omissions));
        Assert.Equal("Unavailable", projected.Pipeline.Serializers.FfxivCollect); Assert.Equal("NotBuilt", projected.Pipeline.Serializers.Compendium); Assert.Single(projected.Pipeline.Fingerprints);
    }
}

