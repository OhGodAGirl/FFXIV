using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using EZDelivery.Bootstrap;
using Xunit;

namespace EZDelivery.Tests.Interface;

public sealed class QualificationCaptureTests
{
    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    [InlineData("spaces here")]
    [InlineData("é")]
    public void InvalidScenarioDoesNotStart(string name)
    {
        // Arrange
        using var capture = new Capture();
        // Act
        var started = capture.Start(name);
        // Assert
        Assert.False(started);
        Assert.Equal(0, capture.Begin());
        Assert.Contains("Start a qualification capture", Assert.Throws<InvalidOperationException>(capture.Export).Message);
    }

    [Fact]
    public void WarmupIsExcludedStreamsAreIndependentAndCaptureStopsAtCapacity()
    {
        // Arrange: call real compiled host helper with deterministic elapsed ticks.
        using var capture = new Capture();
        Assert.True(capture.Start("idle_01"));
        for (var i = 0; i < 512; i++) { capture.Record(false, Stopwatch.Frequency); capture.Record(true, Stopwatch.Frequency); }
        // Act: one stream can finish first; further values must not overwrite it.
        for (var i = 1; i <= 10_000; i++) capture.Record(false, i);
        capture.Record(false, long.MaxValue);
        Assert.Contains("still running", Assert.Throws<InvalidOperationException>(capture.Export).Message);
        for (var i = 1; i <= 10_000; i++) capture.Record(true, i * 2);
        capture.Record(true, long.MaxValue);
        var path = capture.Export();
        using var report = JsonDocument.Parse(File.ReadAllText(path));
        // Assert
        var root = report.RootElement;
        Assert.True(root.GetProperty("Complete").GetBoolean());
        Assert.Equal(0, capture.Begin());
        Assert.Equal("build", root.GetProperty("LoadedBuildId").GetString());
        Assert.Equal(1, root.GetProperty("UpdateCallbacksIgnoredAfterCapacity").GetInt64());
        var update = root.GetProperty("Update");
        Assert.Equal(10_000, update.GetProperty("SampleCount").GetInt32());
        Assert.Equal(512, update.GetProperty("WarmupObserved").GetInt32());
        Assert.Equal(1, update.GetProperty("RawStopwatchTicks")[0].GetInt64());
        Assert.Equal(10_000, update.GetProperty("RawStopwatchTicks")[9999].GetInt64());
        Assert.Equal(9500 * 1000.0 / Stopwatch.Frequency, update.GetProperty("P95Milliseconds").GetDouble());
        Assert.Equal(20_000, root.GetProperty("Draw").GetProperty("RawStopwatchTicks")[9999].GetInt64());
        var rawUpdate = update.GetProperty("RawStopwatchTicks");
        var rawDraw = root.GetProperty("Draw").GetProperty("RawStopwatchTicks");
        for (var i = 0; i < 10_000; i++)
        {
            Assert.Equal(i + 1, rawUpdate[i].GetInt64());
            Assert.Equal((i + 1) * 2, rawDraw[i].GetInt64());
        }
        Assert.True(capture.Start("next"));
    }

    [Fact]
    public void StopExportsIncompleteWithoutClaimingTimingPassAndPreventsDiscardBeforeExport()
    {
        // Arrange
        using var capture = new Capture();
        Assert.True(capture.Start("cancel"));
        // Act
        capture.Record(false, -1);
        capture.Record(false, 10);
        capture.Stop();
        capture.Record(false, 100);
        Assert.False(capture.Start("overwrite"));
        var path = capture.Export();
        using var report = JsonDocument.Parse(File.ReadAllText(path));
        // Assert
        var root = report.RootElement;
        Assert.False(root.GetProperty("Complete").GetBoolean());
        Assert.False(root.GetProperty("Update").GetProperty("MeetsTarget").GetBoolean());
        Assert.Equal(1, root.GetProperty("Update").GetProperty("WarmupObserved").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("Draw").GetProperty("P95Milliseconds").ValueKind);
    }

    [Fact]
    public void OverBudgetSamplesRetainRawOutliersAndFailTargets()
    {
        // Arrange
        using var capture = new Capture();
        capture.Start("slow");
        // Act
        for (var i = 0; i < 10_512; i++)
        {
            capture.Record(false, Stopwatch.Frequency / 10);
            capture.Record(true, Stopwatch.Frequency / 10);
        }
        using var report = JsonDocument.Parse(File.ReadAllText(capture.Export()));
        // Assert
        foreach (var stream in new[] { "Update", "Draw" })
        {
            Assert.False(report.RootElement.GetProperty(stream).GetProperty("MeetsTarget").GetBoolean());
            Assert.Equal(10_000, report.RootElement.GetProperty(stream).GetProperty("SamplesAtLeast50Milliseconds").GetInt32());
        }
    }

    [Fact]
    public void ExportFailurePreservesFrozenCaptureForRetryAndNeverOverwritesExistingFiles()
    {
        // Arrange
        using var capture = new Capture();
        capture.Start("retry");
        capture.Stop();
        var blockingFile = Path.Combine(capture.Directory, "file");
        Directory.CreateDirectory(capture.Directory);
        File.WriteAllText(blockingFile, "retain");
        // Act
        Assert.Throws<IOException>(() => capture.ExportTo(blockingFile, "version", "build"));
        Assert.False(capture.Start("discard"));
        var first = capture.Export();
        var second = capture.Export();
        // Assert
        Assert.NotEqual(first, second);
        Assert.Equal(File.ReadAllText(first), File.ReadAllText(second));
        Assert.Equal("retain", File.ReadAllText(blockingFile));
    }

    [Fact]
    public void TimingTokenBeforeNewCaptureCannotBeRecorded()
    {
        // Arrange
        using var capture = new Capture();
        capture.Start("first");
        var old = capture.Begin();
        capture.Stop();
        capture.Export();
        capture.Start("second");
        // Act
        capture.End(false, old);
        capture.End(false, 0);
        capture.Stop();
        using var report = JsonDocument.Parse(File.ReadAllText(capture.Export()));
        // Assert
        Assert.Equal(0, report.RootElement.GetProperty("Update").GetProperty("WarmupObserved").GetInt32());
    }

    [Fact]
    public void RealTimestampMeasurementHasStableInstanceAndStartBoundaries()
    {
        // Arrange
        using var capture = new Capture();
        var identity = capture.InstanceId;
        // Act
        Assert.False(capture.Start(new string('a', 65)));
        Assert.True(capture.Start(new string('a', 64)));
        Assert.False(capture.Start("already-active"));
        capture.End(false, capture.Begin());
        capture.End(true, capture.Begin());
        capture.Stop();
        capture.Stop();
        using var report = JsonDocument.Parse(File.ReadAllText(capture.Export()));
        // Assert
        Assert.True(Guid.TryParse(identity, out _));
        Assert.Equal(identity, report.RootElement.GetProperty("InstanceId").GetString());
        Assert.Equal(1, report.RootElement.GetProperty("Update").GetProperty("WarmupObserved").GetInt32());
        Assert.Equal(1, report.RootElement.GetProperty("Draw").GetProperty("WarmupObserved").GetInt32());
    }

    [Fact]
    public void DrawFirstCapacityAndCompleteUnexportedCaptureStayFrozen()
    {
        // Arrange
        using var capture = new Capture();
        capture.Start("draw-first");
        // Act
        for (var i = 0; i < 10_513; i++) capture.Record(true, 0);
        for (var i = 0; i < 10_512; i++) capture.Record(false, 0);
        Assert.False(capture.Start("discard"));
        using var report = JsonDocument.Parse(File.ReadAllText(capture.Export()));
        // Assert
        Assert.Equal(1, report.RootElement.GetProperty("DrawCallbacksIgnoredAfterCapacity").GetInt64());
        Assert.True(report.RootElement.GetProperty("Update").GetProperty("MeetsTarget").GetBoolean());
        Assert.True(report.RootElement.GetProperty("Draw").GetProperty("MeetsTarget").GetBoolean());
    }

    [Fact]
    public void ExportPreservesIdentityScopeAndReadableSchemaForSingleCharacterScenario()
    {
        // Arrange
        using var capture = new Capture();
        var before = DateTimeOffset.UtcNow;
        Assert.True(capture.Start("x"));
        capture.Stop();
        // Act
        var text = File.ReadAllText(capture.Export());
        using var report = JsonDocument.Parse(text);
        var root = report.RootElement;
        // Assert: release evidence must identify what was measured and remain reviewable.
        Assert.Equal(1, root.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal("version", root.GetProperty("PluginVersion").GetString());
        Assert.Equal("build", root.GetProperty("LoadedBuildId").GetString());
        Assert.Equal("x", root.GetProperty("Scenario").GetString());
        Assert.Equal(Guid.Parse(capture.InstanceId).ToString("D"), root.GetProperty("InstanceId").GetString());
        Assert.Equal(Stopwatch.Frequency, root.GetProperty("StopwatchFrequency").GetInt64());
        Assert.InRange(root.GetProperty("StartedAtUtc").GetDateTimeOffset(), before, DateTimeOffset.UtcNow);
        Assert.Contains("First 10000 samples", root.GetProperty("CapturePolicy").GetString());
        Assert.Contains("not GPU or whole-frame", root.GetProperty("MeasurementScope").GetString());
        Assert.Contains("\n  \"SchemaVersion\"", text);
    }

    [Fact]
    public void StopIsIdempotentAndOneFullStreamDoesNotMeanComplete()
    {
        // Arrange
        using var capture = new Capture();
        capture.Start("half");
        for (var i = 0; i < 10_512; i++) capture.Record(false, 1);
        for (var i = 0; i < 515; i++) capture.Record(true, 1);
        // Act
        capture.Stop();
        using var first = JsonDocument.Parse(File.ReadAllText(capture.Export()));
        capture.Stop();
        using var second = JsonDocument.Parse(File.ReadAllText(capture.Export()));
        // Assert
        Assert.False(first.RootElement.GetProperty("Complete").GetBoolean());
        Assert.Equal(3, first.RootElement.GetProperty("Draw").GetProperty("SampleCount").GetInt32());
        Assert.Equal(first.RootElement.GetProperty("EndedAtUtc").GetString(), second.RootElement.GetProperty("EndedAtUtc").GetString());
    }

    [Fact]
    public void RealElapsedSampleIsBoundedBySurroundingClockAndZeroTokenAddsNothing()
    {
        // Arrange
        using var capture = new Capture();
        capture.Start("elapsed");
        for (var i = 0; i < 512; i++) capture.Record(false, 0);
        // Act
        var before = Stopwatch.GetTimestamp();
        capture.End(false, capture.Begin());
        var after = Stopwatch.GetTimestamp();
        capture.End(false, 0);
        capture.Stop();
        using var report = JsonDocument.Parse(File.ReadAllText(capture.Export()));
        // Assert
        var stream = report.RootElement.GetProperty("Update");
        Assert.Equal(1, stream.GetProperty("SampleCount").GetInt32());
        Assert.InRange(stream.GetProperty("RawStopwatchTicks")[0].GetInt64(), 0, after - before);
    }

    [Fact]
    public void ExactTargetsPassAndFiftyMillisecondBoundaryIsCounted()
    {
        // Arrange: supported .NET Stopwatch clocks represent integer milliseconds exactly.
        Assert.Equal(0, Stopwatch.Frequency % 1000);
        using var capture = new Capture();
        capture.Start("thresholds");
        for (var i = 0; i < 512; i++) { capture.Record(false, 0); capture.Record(true, 0); }
        // Act: 95% at the target; remaining values include just-below and exact 50 ms.
        for (var i = 0; i < 10_000; i++)
        {
            capture.Record(false, i < 9500 ? Stopwatch.Frequency / 500 : Stopwatch.Frequency / 20);
            capture.Record(true, i < 9500 ? Stopwatch.Frequency / 1000 : Stopwatch.Frequency / 20 - 1);
        }
        using var report = JsonDocument.Parse(File.ReadAllText(capture.Export()));
        // Assert
        var update = report.RootElement.GetProperty("Update");
        var draw = report.RootElement.GetProperty("Draw");
        Assert.Equal(2, update.GetProperty("P95Milliseconds").GetDouble());
        Assert.Equal(1, draw.GetProperty("P95Milliseconds").GetDouble());
        Assert.True(update.GetProperty("MeetsTarget").GetBoolean());
        Assert.True(draw.GetProperty("MeetsTarget").GetBoolean());
        Assert.Equal(500, update.GetProperty("SamplesAtLeast50Milliseconds").GetInt32());
        Assert.Equal(0, draw.GetProperty("SamplesAtLeast50Milliseconds").GetInt32());
    }

    [Fact]
    public void TimestampEqualToCaptureStartIsAccepted()
    {
        // Arrange: Stopwatch does not promise distinct values for adjacent reads.
        using var capture = new Capture();
        capture.Start("equal-clock-tick");
        var timestamp = capture.Begin();
        capture.SetStartTimestamp(timestamp);
        // Act
        capture.End(false, timestamp);
        capture.Stop();
        using var report = JsonDocument.Parse(File.ReadAllText(capture.Export()));
        // Assert: equality belongs to this capture, not to an earlier generation.
        Assert.Equal(1, report.RootElement.GetProperty("Update").GetProperty("WarmupObserved").GetInt32());
    }

    private sealed class Capture : IDisposable
    {
        private readonly object value = new QualificationCapture();
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "EZDelivery-qualification-test-" + Guid.NewGuid().ToString("N"));
        internal string InstanceId => (string)value.GetType().GetProperty("InstanceId", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value)!;
        internal void SetStartTimestamp(long ticks) => value.GetType().GetField("startedTicks", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(value, ticks);
        internal Func<string, bool> Start => Bind<Func<string, bool>>("Start");
        internal Func<long> Begin => Bind<Func<long>>("Begin");
        internal Action<bool, long> End => Bind<Action<bool, long>>("End");
        internal Action<bool, long> Record => Bind<Action<bool, long>>("Record");
        internal Action Stop => Bind<Action>("Stop");
        internal Func<string, string, string, string> ExportTo => Bind<Func<string, string, string, string>>("Export");
        internal string Export() => ExportTo(Directory, "version", "build");
        private T Bind<T>(string name) where T : Delegate => value.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.CreateDelegate<T>(value);
        public void Dispose() { if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true); }
    }
}
