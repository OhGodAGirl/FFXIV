using System.Collections.Immutable;
using Exportaramus.Bootstrap;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Diagnostics;
using Exportaramus.Features.Main;
using Exportaramus.Features.Snapshots;
using Exportaramus.Tests.Snapshots;

namespace Exportaramus.Tests.Diagnostics;

[Collection(Exportaramus.Tests.Performance.OfflinePerformanceCollection.Name)]
public sealed class DiagnosticTelemetryTests
{
    [Fact]
    public async Task ReportWorkerPublishesItsMeasuredPerformanceMetric()
    {
        var report = new DiagnosticReport { GeneratedAtUtc = SnapshotFixture.Now.UtcDateTime };
        DiagnosticReportSerializer.Prepare(report);
        var measurements = new DiagnosticMeasurements();
        await using var controller = new DiagnosticsController(_ => Task.FromResult(report), measurements, _ => { });

        controller.Dispatch(DiagnosticsIntent.GenerateReport);
        await DiagnosticWorkerTest.DrainAsync(controller);

        Assert.NotNull(controller.View.Report);
        var measured = Assert.Single(measurements.Snapshot());
        Assert.Equal("DiagnosticReport", measured.Key); Assert.Equal(1, measured.Value.SampleCount);
        Assert.True(measured.Value.AllocatedBytes.Maximum > 0); Assert.True(measured.Value.DurationMilliseconds.Maximum > 0);
    }
    [Fact]
    public void TimingMeasuresActualWorkAndBoundsFailuresWithoutExceptionMessages()
    {
        var measurements = new DiagnosticMeasurements();
        var value = measurements.Measure("Serializer", () => new byte[4096]);
        Assert.Equal(4096, value.Length); Assert.True(measurements.Snapshot()["Serializer"].AllocatedBytes.Maximum >= 4096);
        Assert.Throws<IOException>(() => measurements.Measure<int>("Writer", () => throw new IOException("D:/private/identity")));
        var failure = Assert.Single(measurements.Failures); Assert.Equal("IOException", failure.ExceptionType); Assert.DoesNotContain("private", failure.Message);
        Assert.Equal(measurements.Snapshot()["Writer"].DurationMilliseconds.Maximum, measurements.LatestMilliseconds["Writer"]);
        using (new DiagnosticTiming(measurements, "AsyncUpperBound", true)) _ = new byte[64];
        Assert.Contains("AsyncUpperBound", measurements.LatestMilliseconds.Keys);
        measurements.Record("TooLarge", 1, 3600001); Assert.True(measurements.HasUnrepresentableSamples); Assert.DoesNotContain("TooLarge", measurements.LatestMilliseconds.Keys);
        measurements.ResetMeasurements(); Assert.False(measurements.HasUnrepresentableSamples); Assert.Empty(measurements.LatestMilliseconds); Assert.Single(measurements.Failures);
    }

    [Theory]
    [InlineData(null, "NotStarted")]
    [InlineData(TaskStatus.Created, "NotStarted")]
    [InlineData(TaskStatus.Running, "Running")]
    [InlineData(TaskStatus.WaitingForActivation, "Waiting")]
    [InlineData(TaskStatus.WaitingToRun, "Waiting")]
    [InlineData(TaskStatus.WaitingForChildrenToComplete, "Waiting")]
    [InlineData(TaskStatus.RanToCompletion, "Completed")]
    [InlineData(TaskStatus.Canceled, "Cancelled")]
    [InlineData(TaskStatus.Faulted, "Faulted")]
    public void TaskStatusMappingUsesTheDiagnosticSchema(TaskStatus? status, string expected) => Assert.Equal(expected, DiagnosticTaskPolicy.State(status));

    [Fact]
    public void RuntimeEnrichmentUsesRealInputsAndNamesEveryUnobservedSection()
    {
        var input = MainSnapshotPresenter.DiagnosticInput(null, "0.1.0.0", "10.0.0", Guid.Empty, false, false, 0, new(0, 0, 0), new(), SnapshotFixture.Now);
        var runtime = new DiagnosticRuntime(); var lifecycle = new DiagnosticLifecycle(); var operation = new DiagnosticOperations();
        var initial = DiagnosticRuntimeEnricher.Apply(input, runtime, lifecycle, operation, new(), ImmutableDictionary<string, double>.Empty, false);
        Assert.Equal(new[] { "ui", "runtime", "storage", "performance", "collectionPipeline" }, initial.IncompleteSections);
        input = input with { Pipeline = input.Pipeline with { CatalogCache = new(null, "Ready", 0) }, Sources = [new("mounts", "Supported", "Fresh", "Complete", "Observed", 0, 0, "None")] };
        var storage = new DiagnosticStorage { LastWrite = new("Succeeded", 24, 2, null), CanonicalAvailable = true, CanonicalBytes = 24 };
        var measured = ImmutableDictionary<string, double>.Empty.Add("FullCapture", 4).Add("LightweightCapture", 1).Add("CatalogBuild", 15).Add("Source.mounts", 2);
        var result = DiagnosticRuntimeEnricher.Apply(input, runtime with { FfxivClientVersion = "2026.08.11.0000.0000" }, lifecycle, operation, storage, measured, false);
        Assert.Equal(new[] { "ui" }, result.IncompleteSections); Assert.Equal(15, result.Pipeline.CatalogCache.BuildMilliseconds); Assert.Equal(2, result.Sources[0].CaptureMilliseconds); Assert.Same(storage, result.Storage);
        Assert.Contains("performance", DiagnosticRuntimeEnricher.Apply(input, runtime, lifecycle, operation, storage, measured, true).IncompleteSections);
        Assert.Contains("performance", DiagnosticRuntimeEnricher.Apply(input, runtime, lifecycle, operation, storage, measured.Remove("LightweightCapture"), false).IncompleteSections);
        Assert.Contains("sourceHealth", DiagnosticRuntimeEnricher.Apply(input, runtime, lifecycle, operation, storage, measured.Remove("Source.mounts"), false).IncompleteSections);
        var stale = input with { Sources = [input.Sources[0] with { SourceStatus = "Stale" }] };
        Assert.Contains("sourceHealth", DiagnosticRuntimeEnricher.Apply(stale, runtime, lifecycle, operation, storage, measured.Remove("Source.mounts"), false).IncompleteSections);
        var unavailable = input with { Sources = [input.Sources[0] with { SourceStatus = "Unavailable" }] };
        Assert.DoesNotContain("sourceHealth", DiagnosticRuntimeEnricher.Apply(unavailable, runtime, lifecycle, operation, storage, measured.Remove("Source.mounts"), false).IncompleteSections);
        DiagnosticReportSerializer.Prepare(DiagnosticReportPreparer.Prepare(result, SnapshotFixture.Now));
    }

    [Theory]
    [InlineData("Never", false, true)]
    [InlineData("Never", true, true)]
    [InlineData("Succeeded", false, true)]
    [InlineData("Succeeded", true, false)]
    [InlineData("Failed", false, true)]
    public void StorageRemainsIncompleteUntilCanonicalMetadataHasBeenObserved(string writeStatus, bool canonicalObserved, bool incomplete)
    {
        var input = MainSnapshotPresenter.DiagnosticInput(null, "0.1.0.0", "10.0.0", Guid.Empty, false, false, 0, new(0, 0, 0), new(), SnapshotFixture.Now);
        var storage = new DiagnosticStorage { LastWrite = new(writeStatus, 2, 1, null), CanonicalAvailable = canonicalObserved };

        var result = DiagnosticRuntimeEnricher.Apply(input, new(), new(), new(), storage, ImmutableDictionary<string, double>.Empty, false);

        Assert.Equal(incomplete, result.IncompleteSections.Contains("storage"));
        Assert.Same(storage, result.Storage);
    }

    [Fact]
    public async Task OwnedConfigurationTaskStatusReflectsActualWork()
    {
        var completion = new TaskCompletionSource();
        await using var configuration = new ConfigurationWriteCoordinator(_ => completion.Task);
        try
        {
            Assert.Null(configuration.WorkerStatus); configuration.Request(true); configuration.Update();
            Assert.Equal(completion.Task.Status, configuration.WorkerStatus);
            completion.SetResult(); configuration.Update(); Assert.Null(configuration.WorkerStatus);
        }
        finally { completion.TrySetResult(); }
    }

    [Fact]
    public void OwnedCatalogTaskStatusReflectsActualFinalizer()
    {
        var completion = new TaskCompletionSource<CollectionCatalog>();
        using var preparation = new CatalogPreparation(() => new BoundedCatalogProjection("English", "version", new List<(string, CatalogItem)>().GetEnumerator(), () => ("English", "version"), () => 0), (_, _) => completion.Task);
        Assert.Null(preparation.WorkerStatus); Assert.False(preparation.Prepare("English", "version")); Assert.Equal(completion.Task.Status, preparation.WorkerStatus);
        completion.SetResult(SnapshotFixture.Create().Catalog with { GameDataVersion = "version" });
        Assert.True(preparation.Prepare("English", "version")); Assert.Null(preparation.WorkerStatus);
    }

    [Fact]
    public async Task StorageCachesRealBytesDurationsRecoveryAndBoundedFailureCodes()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "isolated-test-tmp", Guid.NewGuid().ToString("N"));
        var measurements = new DiagnosticMeasurements(); var storage = new AtomicSnapshotStorage(directory, measurements: measurements);
        try
        {
            Assert.Equal("Never", storage.Diagnostic.LastRead.Status);
            Assert.Null(await storage.ReadAsync(123, false, default)); Assert.Equal("NotFound", storage.Diagnostic.LastRead.Status);
            await storage.CommitAsync(123, "{}", "{\"rich\":true}", null, true, false, SnapshotFixture.Now, default);
            Assert.True(storage.Diagnostic.CanonicalAvailable); Assert.Equal(13, storage.Diagnostic.CanonicalBytes); Assert.Equal(1, storage.Diagnostic.HistoryFileCount); Assert.Equal(13, storage.Diagnostic.HistoryBytes); Assert.Equal(15, storage.Diagnostic.LastWrite.ByteCount);
            Assert.Equal("{}", await storage.ReadAsync(123, false, default)); Assert.Equal(2, storage.Diagnostic.LastRead.ByteCount);
            await storage.CommitAsync(123, "{}", null, "{}", false, false, SnapshotFixture.Now, default); Assert.True(storage.Diagnostic.CanonicalAvailable); Assert.Equal(13, storage.Diagnostic.CanonicalBytes);
            await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => storage.CommitAsync(123, "{", null, null, false, false, SnapshotFixture.Now, default));
            Assert.Equal("WriteFailed", storage.Diagnostic.LastWrite.FailureCode); Assert.Equal("Failed", storage.Diagnostic.LastWrite.Status);
            await File.WriteAllBytesAsync(storage.CharacterPath(123), [0xff]);
            await Assert.ThrowsAsync<System.Text.DecoderFallbackException>(() => storage.ReadAsync(123, false, default)); Assert.Equal("ReadFailed", storage.Diagnostic.LastRead.FailureCode);
            Assert.Contains("FileWrite", measurements.LatestMilliseconds.Keys); Assert.Contains("FileRead", measurements.LatestMilliseconds.Keys);
            Assert.All(measurements.Failures, failure => Assert.DoesNotContain(directory, failure.Message));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void CatalogAndCaptureRecordActiveWorkWithTheInjectedSink()
    {
        var measurements = new DiagnosticMeasurements(); var data = SnapshotFixture.Create();
        using (var projection = new BoundedCatalogProjection("English", "version", new List<(string, CatalogItem)> { ("mounts", new(1, 1, "Mount", false)) }.GetEnumerator(), () => ("English", "version"), () => 0, measurements))
            Assert.True(projection.Advance());
        using (var capture = new CollectionReadOperation(new Reader(), data.Catalog, true, 123, 1, SnapshotFixture.Now, measurements))
            CompleteFixtureCapture(capture, 1);
        using (var capture = new CollectionReadOperation(new Reader(), data.Catalog, false, 123, 1, SnapshotFixture.Now, measurements))
            CompleteFixtureCapture(capture, 128);
        var metrics = measurements.Snapshot(); Assert.Contains("CatalogProjectionSlice", metrics.Keys); Assert.Contains("FullCapture", metrics.Keys); Assert.Contains("LightweightCapture", metrics.Keys); Assert.Contains("Source.mounts", metrics.Keys); Assert.Contains("Source.armoire", metrics.Keys);
        Assert.True(metrics["FullCapture"].DurationMilliseconds.Maximum >= metrics["FullCaptureSlice"].DurationMilliseconds.P50);
    }
    private static void CompleteFixtureCapture(CollectionReadOperation capture, int rowBudget)
    {
        // The fixture has one catalog row per collection and seven physical sources.
        // 128 steps allow every row/source transition even with a one-row slice.
        var completed = false;
        for (var step = 0; step < 128 && !completed; step++) completed = capture.Advance(rowBudget);
        Assert.True(completed, "Fixture capture must finish within 128 bounded steps.");
    }
    private sealed class Reader : ICollectionReader
    {
        public bool IdentityMatches(ulong contentId) => true;
        public bool IsAvailable(string key, string version) => true;
        public bool ReadUnlock(string key, CatalogItem row) => true;
        public QuestState ReadQuest(uint id, bool completed) => new(id, QuestStatus.Completed, null);
        public CurrentItemPresenceState ReadPresence(string source, CollectionCatalog catalog, ulong contentId, long generation, DateTimeOffset now) => CaptureReadPolicy.Presence([], source is not ("armoire" or "glamourDresser"), true, generation, now);
    }
}
