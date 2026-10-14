using System.Diagnostics;
using System.Text.Json;
using Exportaramus.Features.CollectionData.Contracts;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.State;
using Harness = Exportaramus.Tests.Snapshots.ControllerSchedulingIntegrationTests.Harness;

namespace Exportaramus.Tests.Snapshots;

public sealed class ControllerAdditionalBoundaryTests
{
    [Fact]
    public async Task AnIncompleteNativeReadRemainsCapturingUntilThePortFinishes()
    {
        await using var run = new Harness();
        await run.Restore(0);
        run.Port.AdvanceReady = false;
        run.Controller.RequestFull(); run.Update(5);
        run.Update(6);
        Assert.Equal("Capturing", run.Controller.DiagnosticOperation(6).ControllerState);
        Assert.Null(run.Controller.Publication.Current);
        Assert.Null(run.Controller.Error);
        Assert.Single(run.Port.Begins);
        run.Port.AdvanceReady = true;
        await run.Accept(7, 1, CaptureType.Full);
    }

    [Fact]
    public async Task ANoticeWaitingForCatalogReadinessCannotEscapeIntoTheNextCharacter()
    {
        await using var run = new Harness();
        var notices = new List<string>(); run.Controller.Changed += notices.Add;
        await run.Restore(0);
        run.Controller.RequestFull(); run.Update(5); run.Update(5);
        var clock = Stopwatch.StartNew();
        while (run.Controller.DiagnosticTasks.Single(task => task.Name == "CollectionOutput").State != "Completed")
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(5)) Assert.Fail("Output preparation did not complete.");
            await Task.Delay(1);
        }
        run.Port.CatalogReady = false;
        run.Update(5);
        Assert.NotNull(run.Controller.Publication.Current);
        Assert.Empty(notices);
        run.Port.CatalogReady = true;
        run.Controller.Update(456, "Second", false, false, 6, SnapshotFixture.Now.AddSeconds(6));
        clock.Restart();
        while (!run.Controller.Reports.Available || run.Controller.Busy)
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(5)) Assert.Fail("Second character restoration did not complete.");
            run.Controller.Update(456, "Second", false, false, 6, SnapshotFixture.Now.AddSeconds(6));
            await Task.Delay(1);
        }
        Assert.Equal(456ul, run.Controller.ActiveContentId);
        Assert.Null(run.Controller.Publication.Current);
        Assert.Empty(notices);
    }

    [Fact]
    public async Task IdenticalFullAndDuplicateReportStillNameEveryObservedSource()
    {
        await using var run = new Harness();
        var notices = new List<string>(); run.Controller.Changed += notices.Add;
        await run.Restore(0);
        run.Controller.RequestFull(); await run.Accept(5, 1, CaptureType.Full);
        notices.Clear();
        run.Controller.RequestFull(); await run.Accept(10, 2, CaptureType.Full);
        using (var document = JsonDocument.Parse(Assert.Single(notices)))
            Assert.Equal(CollectionKeys.Unlocks.Where(key => key is not ("facewear" or "beasts")).Concat(CollectionReadOperation.PhysicalSources).Order(StringComparer.Ordinal),
                document.RootElement.GetProperty("changedSources").EnumerateArray().Select(value => value.GetString()));
        await run.Report(20, 3);
        notices.Clear();
        await run.Report(21, 4);
        using var reported = JsonDocument.Parse(Assert.Single(notices));
        Assert.Equal(new[] { "reportedOwnership" }, reported.RootElement.GetProperty("changedSources").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task RestoredSnapshotExplainsWhyCollectCopyRequiresAFreshSync()
    {
        await using var run = new Harness();
        await run.Storage.CommitAsync(123, CollectionDataSerializer.Compact(SnapshotFixture.Create(), 8), null, null,
            false, false, SnapshotFixture.Now, default);
        await run.Restore(0);
        var export = run.Controller.Publication.Current!.Export;
        Assert.False(export.CanCopy);
        Assert.False(string.IsNullOrWhiteSpace(export.Error));
        Assert.Contains("Sync", export.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HistoryNamesPreserveWhetherTheAcceptedCaptureWasManuallyRequested(bool manual)
    {
        await using var run = new Harness();
        await run.Restore(0);
        if (manual) run.Controller.RequestFull();
        await run.Accept(manual ? 5 : 15, 1, CaptureType.Full);
        var historyRoot = Path.Combine(Path.GetDirectoryName(run.Storage.CanonicalPath)!, "snapshots");
        var file = Assert.Single(Directory.GetFiles(historyRoot, "*-snapshot-*.json"));
        Assert.StartsWith(manual ? "manual-snapshot-" : "automatic-snapshot-", Path.GetFileName(file));
    }

    [Fact]
    public async Task LogoutClearsPendingRequestsAndLeavesNoCaptureInProgress()
    {
        await using var run = new Harness();
        await run.Restore(0);
        run.Controller.RequestFull();
        Assert.Equal("ManualFull", run.Controller.DiagnosticOperation(0).PendingRequestPriority);
        run.Controller.Update(0, "", true, false, 1, SnapshotFixture.Now);
        Assert.Equal("None", run.Controller.DiagnosticOperation(1).PendingRequestPriority);
        Assert.False(run.Controller.Busy);
        Assert.Equal("Idle", run.Controller.DiagnosticOperation(1).ControllerState);
    }
}

