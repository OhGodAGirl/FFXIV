using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Exportaramus.Features.Diagnostics;

namespace Exportaramus.Tests.Diagnostics;

public sealed class DiagnosticsTests
{
    private static DiagnosticReport Report => new() { GeneratedAtUtc = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc) };
    [Fact]
    public void ReportDisplayLabelsArePreparedFromTheAcceptedBytes()
    {
        var prepared = DiagnosticReportSerializer.Prepare(Report);
        Assert.Equal("Generated: 2026-09-06 12:00:00Z", prepared.GeneratedText);
        Assert.Equal($"Report size: {prepared.Utf8Bytes:N0} bytes", prepared.SizeText);
        Assert.Equal("Report fingerprint: " + prepared.Model.FingerprintSha256, prepared.FingerprintText);
        Assert.All(prepared.Sections, section => Assert.Equal("DiagnosticSection" + section.Heading, section.TableId));
    }
    [Fact]
    public void MaintainedDiagnosticFixturesPassSchemaRedactionAndFingerprintValidation()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "project", "Exportaramus.csproj"))) directory = directory.Parent!;
        var fixtures = Path.Combine(directory.FullName, "tests", "fixtures", "Diagnostics", "1.0", "fixtures");
        foreach (var file in Directory.EnumerateFiles(Path.Combine(fixtures, "valid"), "*.json"))
            DiagnosticReportSerializer.ValidateWireJson(File.ReadAllText(file));
        foreach (var file in Directory.EnumerateFiles(Path.Combine(fixtures, "invalid"), "*.json"))
            Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.ValidateWireJson(File.ReadAllText(file)));
    }
    [Fact]
    public async Task ReportSourceUsesOneImmutableMetadataSnapshotAndHonorsCancellation()
    {
        var measurements = new DiagnosticMeasurements(); var source = new DiagnosticReportSource(measurements);
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadAsync(CancellationToken.None));
        source.Set(Report); measurements.Record("Draw", 1, 10);
        var snapshot = await source.ReadAsync(CancellationToken.None);
        Assert.Equal(Report.Build, snapshot.Build); Assert.True(snapshot.GeneratedAtUtc > Report.GeneratedAtUtc); Assert.Single(snapshot.Performance);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => source.ReadAsync(cancellation.Token));
    }
    [Fact]
    public void WireValidatorRejectsProhibitedFieldsTextFingerprintAndOversize()
    {
        var prepared = DiagnosticReportSerializer.Prepare(Report);
        DiagnosticReportSerializer.ValidateWireJson(prepared.Json);
        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.ValidateWireJson("{\"contentId\":\"7\"}"));
        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.ValidateWireJson("{\"text\":\"alice@example.com\"}"));
        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.ValidateWireJson(prepared.Json.Replace(prepared.Model.FingerprintSha256, new string('f', 64), StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.ValidateWireJson(new string(' ', DiagnosticReportSerializer.MaximumUtf8Bytes + 1)));
        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.ValidateWireJson("{}"));
        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.Prepare(Report with { Storage = new() { HistoryFileCount = 26 } }));
        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.ValidateWireJson(prepared.Json.Replace("\"incompleteSections\":[]", "\"incompleteSections\":[\"storage\"]", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.ValidateWireJson(prepared.Json.Replace("\"reportStatus\":\"Ready\"", "\"reportStatus\":\"Partial\"", StringComparison.Ordinal)));
        DiagnosticReportSerializer.ValidateWireJson(DiagnosticReportSerializer.Prepare(Report with { ReportStatus = "Failed" }).Json);
        DiagnosticReportSerializer.ValidateWireJson(DiagnosticReportSerializer.Prepare(Report with { ReportStatus = "Partial", IncompleteSections = ["storage"] }).Json);
        Assert.False(DiagnosticRedactionPolicy.IsSafe(new string('a', 513)));
        Assert.True(DiagnosticRedactionPolicy.IsSafe(new string('a', 512)));
    }
    [Fact]
    public void EveryFailureFieldIsScannedBeforeItEntersReportSerialization()
    {
        var failure = DiagnosticRedactionPolicy.Failure(Report.GeneratedAtUtc, "Storage", "Write", "WriteFailed", "IOException");
        DiagnosticFailure[] invalid = [failure with { Component = "bad/path" }, failure with { Operation = "bad/path" },
            failure with { Code = "bad/path" }, failure with { ExceptionType = "bad/path" }];
        foreach (var value in invalid) Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.Prepare(Report with { RecentFailures = [value] }));
        var frame = new DiagnosticFrame("Exportaramus", "Storage", "Write", "File.cs", 1);
        DiagnosticFrame[] invalidFrames = [frame with { Assembly = "alice@example.com" }, frame with { Type = "alice@example.com" },
            frame with { Method = "alice@example.com" }, frame with { SourceFileName = "alice@example.com" }];
        foreach (var value in invalidFrames) Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.Prepare(Report with { RecentFailures = [failure with { Frames = [value] }] }));
        DiagnosticReportSerializer.Prepare(Report with
        {
            RecentFailures = [failure with { Frames = [frame with { SourceFileName = null, Line = null }] }],
            CollectionPipeline = new() { Omissions = [new("mounts", "CaptureFailed")] },
            SourceHealth = [new("mounts", "UnavailablePendingValidation", "NeverObserved", "Unknown", "None", null, 0, "PatchValidationRequired")]
        });
    }
    [Fact]
    public void ReportFingerprintExcludesOnlyRootTimestampAndFingerprintAndPropertiesAreOrdinal()
    {
        var report = DiagnosticReportSerializer.Prepare(Report);
        var later = DiagnosticReportSerializer.Prepare(Report with { GeneratedAtUtc = Report.GeneratedAtUtc.AddDays(1), FingerprintSha256 = new('f', 64) });
        Assert.Equal(report.Model.FingerprintSha256, later.Model.FingerprintSha256); Assert.NotEqual(report.Json, later.Json);
        var changed = DiagnosticReportSerializer.Prepare(Report with { Lifecycle = new() { Revision = 2 } });
        Assert.NotEqual(report.Model.FingerprintSha256, changed.Model.FingerprintSha256);
        using var parsed = JsonDocument.Parse(report.Json);
        var expected = Convert.ToHexStringLower(SHA256.HashData(DiagnosticReportSerializer.Canonical(parsed.RootElement, true)));
        Assert.Equal(expected, report.Model.FingerprintSha256); Assert.Equal(Encoding.UTF8.GetByteCount(report.Json), report.Utf8Bytes);
        Assert.Equal(parsed.RootElement.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal), parsed.RootElement.EnumerateObject().Select(x => x.Name));
        Assert.Equal(9, report.Sections.Length); Assert.Equal(new[] { "Lifecycle and Operations", "Performance", "UI", "Recent Failures" }, report.Sections.Where(x => x.InitiallyOpen).Select(x => x.Heading));
    }
    [Fact]
    public void PartialReportsDeclareIncompleteSectionsAndRemainCopyable()
    {
        var partial = DiagnosticReportSerializer.Prepare(Report with { ReportStatus = "Partial", IncompleteSections = ["storage"] });
        Assert.Equal("Partial", partial.Model.ReportStatus);
        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.Prepare(Report with { IncompleteSections = ["storage"] }));
        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.Prepare(Report with { ReportStatus = "Partial" }));
    }
    [Theory]
    [InlineData("C:\\Users\\alice\\secret.txt")]
    [InlineData("/home/alice/file")]
    [InlineData("alice@example.com")]
    [InlineData("0x1234DEADBEEF")]
    [InlineData("token=hidden")]
    [InlineData("ContentId:12345678901234567")]
    [InlineData("itemId=55")]
    [InlineData("hello\nworld")]
    [InlineData("\\\\host\\share")]
    public void ProhibitedFailureDataIsNeverRetained(string raw)
    {
        Assert.False(DiagnosticRedactionPolicy.IsSafe(raw));
        var redacted = DiagnosticRedactionPolicy.Failure(Report.GeneratedAtUtc, raw, raw, raw, raw);
        Assert.Equal("Unknown", redacted.Component); Assert.Equal("Unknown", redacted.Operation); Assert.Equal("OperationFailed", redacted.Code); Assert.Null(redacted.ExceptionType);
        Assert.DoesNotContain(raw, DiagnosticReportSerializer.Prepare(Report with { RecentFailures = [redacted] }).Json);
        var unredacted = redacted with { Message = raw };
        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.Prepare(Report with { RecentFailures = [unredacted] }));
    }
    [Fact]
    public void RawMetadataPathsAndProhibitedDictionaryFieldsFailTheReportGate()
    {
        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.Prepare(Report with { Runtime = new() { OsVersion = "C:\\Users\\user" } }));
        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.Prepare(Report with { Runtime = new() { Dependencies = ImmutableDictionary<string, string>.Empty.Add("contentId", "redacted") } }));
        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.Prepare(Report with { Runtime = new() { Dependencies = ImmutableDictionary<string, string>.Empty.Add("invalid/name", "redacted") } }));
        var failure = DiagnosticRedactionPolicy.Failure(Report.GeneratedAtUtc, "Storage", "Write", "WriteFailed", "IOException");
        Assert.Equal("IOException", failure.ExceptionType);
        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.Prepare(Report with { RecentFailures = [failure with { Frames = [new("Exportaramus", "Storage", "Write", "folder/file.cs", 1)] }] }));
        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.Prepare(Report with { RecentFailures = [failure with { Frames = [new("Exportaramus", "Storage", "Write", "folder\\file.cs", 1)] }] }));
        var valid = DiagnosticReportSerializer.Prepare(Report with { RecentFailures = [failure with { Frames = [new("Exportaramus", "Storage", "Write", "File.cs", 1)] }], Lifecycle = new() { OwnedTasks = [new("Output", "Running")] } });
        Assert.Contains("File.cs", valid.Json); Assert.Contains(valid.Sections, x => x.Heading == "Recent Failures" && x.Rows.Any());
    }
    [Fact]
    public void FixedFailureCapacityAndTimingSampleCapacityEvictOldestOnly()
    {
        var measurements = new DiagnosticMeasurements();
        for (var i = 0; i < 300; i++) measurements.Record("Draw", i, i * 2);
        var timing = measurements.Snapshot()["Draw"];
        Assert.Equal(256, timing.SampleCount); Assert.Equal(299, timing.DurationMilliseconds.Maximum); Assert.Equal(598, timing.AllocatedBytes.Maximum); Assert.Equal(171, timing.DurationMilliseconds.P50);
        for (var i = 0; i < 25; i++) measurements.RecordFailure(DiagnosticRedactionPolicy.Failure(Report.GeneratedAtUtc.AddSeconds(i), "Storage", "Write", "WriteFailed", null));
        Assert.Equal(20, measurements.Failures.Length); Assert.Equal(Report.GeneratedAtUtc.AddSeconds(5), measurements.Failures[0].OccurredAtUtc);
        measurements.CountReport(true, false); measurements.CountReport(false, true);
        Assert.Equal(new DiagnosticCounters(1, 1, 1), measurements.Counters);
        measurements.ResetMeasurements(); Assert.Empty(measurements.Snapshot()); Assert.Equal(new DiagnosticCounters(0, 0, 0), measurements.Counters); Assert.Equal(20, measurements.Failures.Length);
    }
    [Fact]
    public void FailureBufferRejectsRawFailureTextAtItsOwnBoundary()
    {
        var measurements = new DiagnosticMeasurements();
        measurements.RecordFailure(new(Report.GeneratedAtUtc, "C:\\Users\\someone", "Write", "WriteFailed", "IOException",
            "password=secret", [new("Assembly", "Type", "Method", "C:\\Users\\someone\\Source.cs", 1)]));
        var retained = Assert.Single(measurements.Failures);
        Assert.Equal("Unknown", retained.Component); Assert.DoesNotContain("secret", retained.Message); Assert.Empty(retained.Frames);
    }
    [Fact]
    public void SamplingRejectsInvalidUnboundedInputAndBoundsMetricCardinality()
    {
        var measurements = new DiagnosticMeasurements();
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, -1, 3600001 }) { measurements.Record("Draw", value, 1); measurements.Record("Draw", 1, value); }
        measurements.Record("invalid/name", 1, 1); Assert.Empty(measurements.Snapshot());
        for (var i = 0; i < 65; i++) measurements.Record("Metric" + i, 0, 3600000);
        Assert.Equal(64, measurements.Snapshot().Count);
        Assert.Equal(new DiagnosticPercentiles(0, 0, 0), DiagnosticMeasurements.Percentiles([]));
        Assert.Equal(new DiagnosticPercentiles(1, 2, 2), DiagnosticMeasurements.Percentiles([2, 1]));
    }
    [Fact]
    public void CardinalityAndWholeReportSizeLimitsFailWithoutTruncating()
    {
        var failure = DiagnosticRedactionPolicy.Failure(Report.GeneratedAtUtc, "Storage", "Write", "WriteFailed", null);
        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.Prepare(Report with { RecentFailures = Enumerable.Repeat(failure, 21).ToImmutableArray() }));
        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.Prepare(Report with { SourceHealth = Enumerable.Repeat(new DiagnosticSource("mounts", "Supported", "Fresh", "Complete", "Observed", 0, 0, "None"), 65).ToImmutableArray() }));
        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.Prepare(Report with { Lifecycle = new() { OwnedTasks = Enumerable.Repeat(new DiagnosticTask("Output", "Running"), 33).ToImmutableArray() } }));
        var frames = Enumerable.Repeat(new DiagnosticFrame(new('a', 256), new('b', 256), new('c', 256), "Source.cs", 1), 32).ToImmutableArray();
        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.Prepare(Report with { RecentFailures = Enumerable.Repeat(failure with { Frames = frames }, 20).ToImmutableArray() }));
    }
    [Fact]
    public async Task ControllerRunsOneWorkerCopiesPreparedReportAndResetsMeasurementsOnly()
    {
        var measurements = new DiagnosticMeasurements(); var completion = new TaskCompletionSource<DiagnosticReport>(); var calls = 0; string? copied = null;
        await using var controller = new DiagnosticsController(_ => { Interlocked.Increment(ref calls); return completion.Task; }, measurements, value => copied = value);
        try
        {
            Assert.Equal(new DiagnosticTask("Diagnostics", "NotStarted"), controller.OwnedTask);
            Assert.Equal("Ready", controller.View.Status); Assert.Equal("", controller.View.Error); Assert.False(controller.View.ReduceMotion);
            Assert.True(controller.View.CanGenerate); Assert.False(controller.View.CanCopy); controller.Dispatch(DiagnosticsIntent.CopyReport); Assert.Null(copied);
            controller.Dispatch((DiagnosticsIntent)999);
            controller.Dispatch(DiagnosticsIntent.GenerateReport); controller.Dispatch(DiagnosticsIntent.GenerateReport);
            await Task.Run(controller.Update).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(controller.View.Running); Assert.False(controller.View.CanGenerate);
            Assert.Equal("Running", controller.View.Status); Assert.Equal("", controller.View.Error);
            Assert.Equal("Diagnostics", controller.OwnedTask.Name); Assert.NotEqual("NotStarted", controller.OwnedTask.State);
            completion.SetResult(Report); await DiagnosticWorkerTest.DrainAsync(controller); Assert.Equal(1, calls); Assert.True(controller.View.CanCopy); Assert.False(controller.View.Running);
            var prepared = controller.View.Report; controller.Dispatch(DiagnosticsIntent.CopyReport); Assert.Same(prepared!.Json, copied);
            measurements.Record("Draw", 1, 1); controller.Dispatch(DiagnosticsIntent.ResetMeasurements); Assert.Empty(measurements.Snapshot()); Assert.Same(prepared, controller.View.Report);
            controller.SetReduceMotion(true); Assert.True(controller.View.ReduceMotion); controller.Dispatch(DiagnosticsIntent.Close); Assert.Equal("Ready", controller.View.Status);
        }
        finally
        {
            // An assertion can fail before publication; release the injected source before disposal waits for it.
            completion.TrySetResult(Report);
        }
    }
    [Fact]
    public async Task ClosingOrDisposingDuringReportPreventsStalePublicationAndObservesWorker()
    {
        var completion = new TaskCompletionSource<DiagnosticReport>();
        var controller = new DiagnosticsController(_ => completion.Task, new(), _ => { });
        Task? disposal = null;
        try
        {
            controller.Dispatch(DiagnosticsIntent.GenerateReport); controller.Dispatch(DiagnosticsIntent.Close); completion.SetResult(Report); await DiagnosticWorkerTest.DrainAsync(controller);
            Assert.Null(controller.View.Report); Assert.False(controller.View.Running);
            disposal = controller.DisposeAsync().AsTask();
            await disposal.WaitAsync(TimeSpan.FromSeconds(3));
            await controller.DisposeAsync(); controller.Dispatch(DiagnosticsIntent.GenerateReport); Assert.False(controller.View.Running);
        }
        finally
        {
            completion.TrySetResult(Report);
            disposal ??= controller.DisposeAsync().AsTask();
            await disposal.WaitAsync(TimeSpan.FromSeconds(3));
        }

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken sourceToken = default;
        var release = new TaskCompletionSource<DiagnosticReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = new DiagnosticsController(async token =>
        {
            sourceToken = token;
            entered.TrySetResult();
            return await release.Task.WaitAsync(token);
        }, new(), _ => { });
        Task? runningDisposal = null;
        try
        {
            running.Dispatch(DiagnosticsIntent.GenerateReport); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            runningDisposal = running.DisposeAsync().AsTask();
            Assert.True(sourceToken.IsCancellationRequested, "Disposal must cancel its active report source before waiting for completion.");
            await runningDisposal.WaitAsync(TimeSpan.FromSeconds(3)); running.Update(); Assert.Null(running.View.Report); Assert.False(running.View.Running);
        }
        finally
        {
            release.TrySetResult(Report);
            runningDisposal ??= running.DisposeAsync().AsTask();
            // Repeated DisposeAsync calls return immediately; observe the original task instead.
            await runningDisposal.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }
    [Fact]
    public async Task FailedGenerationRetainsPreviousPreparedReportAndReportsBoundedError()
    {
        var fail = false;
        var measurements = new DiagnosticMeasurements();
        await using var controller = new DiagnosticsController(_ => fail ? Task.FromException<DiagnosticReport>(new IOException("secret C:\\Users\\someone")) : Task.FromResult(Report), measurements, _ => { });
        controller.Dispatch(DiagnosticsIntent.GenerateReport); await DiagnosticWorkerTest.DrainAsync(controller); var previous = controller.View.Report;
        fail = true; controller.Dispatch(DiagnosticsIntent.GenerateReport); await DiagnosticWorkerTest.DrainAsync(controller);
        Assert.Equal("Failed", controller.View.Status); Assert.Same(previous, controller.View.Report); Assert.DoesNotContain("secret", controller.View.Error);
        Assert.False(string.IsNullOrWhiteSpace(controller.View.Error));
        var failure = Assert.Single(measurements.Failures);
        Assert.Equal("Diagnostics", failure.Component); Assert.Equal("GenerateReport", failure.Operation); Assert.Equal("ReportFailed", failure.Code);
        fail = false; controller.Dispatch(DiagnosticsIntent.GenerateReport); Assert.Equal("", controller.View.Error); await DiagnosticWorkerTest.DrainAsync(controller);
        Assert.Equal("Ready", controller.View.Status);
    }

    [Fact]
    public async Task ClosingDiagnosticsRetainsTheAcceptedPartialReportStatus()
    {
        await using var controller = new DiagnosticsController(_ => Task.FromResult(Report with { ReportStatus = "Partial", IncompleteSections = ["storage"] }), new(), _ => { });
        controller.Dispatch(DiagnosticsIntent.GenerateReport); await DiagnosticWorkerTest.DrainAsync(controller);
        var accepted = controller.View.Report;

        controller.Dispatch(DiagnosticsIntent.Close);

        Assert.Equal("Partial", controller.View.Status); Assert.Same(accepted, controller.View.Report);
    }
}
