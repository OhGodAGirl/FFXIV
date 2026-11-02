namespace SpringCleaning.Tests;

public sealed class UiFlickerTraceTests
{
    private static readonly DateTime Start = new(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly UiFlickerState Visible = new(5, 5, 5, 0, 0, 1f, 1f, 3, true, 0, 0);

    [Fact]
    public void FirstObservationReportsPresentationEvidenceWithoutInventingSkippedFrames()
    {
        var trace = new UiFlickerTrace();
        var state = new UiFlickerState(5, 1, 4, 2, 8, 0.25f, 0.75f, 3, true, 2, 4);

        var report = trace.Observe(Start, 320, state, 300, 7);

        Assert.Equal($"UI trace frame=320 skipped=0 updates=300 refreshes=7 "
            + $"open=05/01 drawn=04 dim=02 zero=08 entryAlpha={0.25f:F3} "
            + $"exitAlpha={0.75f:F3} native=03 snapshot=True hide=2 show=4.", report);
    }

    [Fact]
    public void DoesNotStartItsBudgetBeforeAWindowIsOpened()
    {
        var trace = new UiFlickerTrace();
        Assert.Null(trace.Observe(Start, 1, default, 1, 0));
        Assert.Contains("open=05/05", trace.Observe(Start.AddHours(1), 2, Visible, 2, 0));
        Assert.False(trace.Finished);
    }

    [Fact]
    public void ConsecutiveLowRateFramesAreNotReportedAsMissingDraws()
    {
        var trace = new UiFlickerTrace();
        trace.Observe(Start, 10, Visible, 10, 0);
        Assert.Null(trace.Observe(Start.AddSeconds(1), 11, Visible, 11, 0));
        Assert.Contains("skipped=1", trace.Observe(Start.AddSeconds(2), 13, Visible, 13, 0));
    }

    [Fact]
    public void SnapshotLossAndPresentationLossAreReportedIndependently()
    {
        var trace = new UiFlickerTrace();
        trace.Observe(Start, 1, Visible, 1, 0);
        var snapshotLost = trace.Observe(Start.AddMilliseconds(10), 2,
            Visible with { SnapshotAvailable = false }, 2, 1);
        Assert.Contains("drawn=05", snapshotLost);
        Assert.Contains("snapshot=False", snapshotLost);
        var hidden = trace.Observe(Start.AddMilliseconds(20), 3,
            Visible with { Drawn = 0, HideEvents = 1, ShowEvents = 1 }, 3, 1);
        Assert.Contains("drawn=00", hidden);
        Assert.Contains("hide=1 show=1", hidden);
    }

    [Fact]
    public void StopsAtTenMinutesAndEmitsOnlyOneTerminalReport()
    {
        var trace = new UiFlickerTrace();
        trace.Observe(Start, 1, Visible, 1, 0);
        Assert.Contains("finished", trace.Observe(Start.AddMinutes(10), 2, Visible, 2, 0));
        Assert.True(trace.Finished);
        Assert.Null(trace.Observe(Start.AddMinutes(11), 3, Visible, 3, 0));
    }

    [Fact]
    public void CapsRapidlyChangingStates()
    {
        var trace = new UiFlickerTrace();
        for (var index = 0; index < 120; index++)
            Assert.NotNull(trace.Observe(Start.AddMilliseconds(index), index + 1,
                Visible with { SnapshotAvailable = index % 2 == 0 }, index, index));
        Assert.Contains("finished", trace.Observe(Start.AddMilliseconds(120), 121, Visible, 121, 121));
        Assert.Null(trace.Observe(Start.AddMilliseconds(121), 122, Visible, 122, 122));
    }

    [Fact]
    public void UnchangedStateProducesSparseSummariesWithRefreshCounts()
    {
        var trace = new UiFlickerTrace();
        trace.Observe(Start, 1, Visible, 1, 0);
        Assert.Null(trace.Observe(Start.AddSeconds(9), 2, Visible, 2, 1));
        Assert.Contains("refreshes=2", trace.Observe(Start.AddSeconds(10), 3, Visible, 3, 2));
    }
}
