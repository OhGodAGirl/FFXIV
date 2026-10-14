using Exportaramus.Features.Diagnostics;

namespace Exportaramus.Tests.Diagnostics;

public sealed class DiagnosticMeasurementBoundaryTests
{
    [Theory]
    [InlineData(true, false, 1, 0, 0)]
    [InlineData(false, false, 0, 1, 0)]
    [InlineData(false, true, 0, 1, 1)]
    [InlineData(true, true, 1, 0, 1)]
    public void IndividualReportOutcomesIncrementOnlyTheirOwnCounters(bool accepted, bool throttled, long acceptedCount, long rejectedCount, long throttledCount)
    {
        var measurements = new DiagnosticMeasurements();

        measurements.CountReport(accepted, throttled);

        Assert.Equal(new DiagnosticCounters(acceptedCount, rejectedCount, throttledCount), measurements.Counters);
    }

    [Fact]
    public void FailedMeasuredWorkRetainsItsUsefulPipelineComponentAndOperation()
    {
        var measurements = new DiagnosticMeasurements();

        Assert.Throws<IOException>(() => measurements.Measure<int>("Serialize", () => throw new IOException("private text")));

        var failure = Assert.Single(measurements.Failures);
        Assert.Equal("Pipeline", failure.Component); Assert.Equal("Serialize", failure.Operation);
        Assert.Equal("OperationFailed", failure.Code); Assert.Equal("IOException", failure.ExceptionType);
    }
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 1, false)]
    [InlineData(3600000, 3600000, false)]
    [InlineData(3600001, 1, true)]
    [InlineData(1, 3600001, true)]
    [InlineData(3600001, 3600001, true)]
    public void FirstSampleIndependentlyDeterminesWhetherPerformanceEvidenceIsRepresentable(double duration, double bytes, bool unrepresentable)
    {
        var measurements = new DiagnosticMeasurements();
        Assert.False(measurements.HasUnrepresentableSamples);

        measurements.Record("Capture", duration, bytes);

        Assert.Equal(unrepresentable, measurements.HasUnrepresentableSamples);
        if (unrepresentable) Assert.Empty(measurements.Snapshot());
        else
        {
            var metric = Assert.Single(measurements.Snapshot()).Value;
            Assert.Equal(1, metric.SampleCount);
            Assert.Equal(duration, metric.DurationMilliseconds.Maximum);
            Assert.Equal(bytes, metric.AllocatedBytes.Maximum);
        }
    }
}
