using Exportaramus.Features.Diagnostics;

namespace Exportaramus.Tests.Diagnostics;

public sealed class DiagnosticsViewStateTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void GenerateRequiresIdleWorkerWhileCopyDependsOnlyOnAcceptedReport(bool running, bool hasReport)
    {
        // Arrange: a replacement worker may run while an earlier accepted report remains copyable.
        var report = hasReport ? DiagnosticReportSerializer.Prepare(new DiagnosticReport
        {
            GeneratedAtUtc = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc),
        }) : null;
        var state = new DiagnosticsViewState(running, running ? "Running" : "Ready", "", report, false);

        // Act.
        var canGenerate = state.CanGenerate;
        var canCopy = state.CanCopy;

        // Assert.
        Assert.Equal(!running, canGenerate);
        Assert.Equal(hasReport, canCopy);
        Assert.Same(report, state.Report);
    }
}
