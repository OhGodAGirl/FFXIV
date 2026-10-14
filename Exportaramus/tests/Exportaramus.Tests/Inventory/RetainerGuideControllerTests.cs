using System.Collections.Immutable;
using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class RetainerGuideControllerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InvalidRosterCannotStartRetainerSelection(int defect)
    {
        var port = new Port
        {
            Roster = defect switch
            {
                0 => default,
                1 => Enumerable.Range(1, 11).Select(i => new InventoryRetainer((ulong)i, "Name")).ToImmutableArray(),
                _ => [new(0, "Invalid")],
            }
        };
        var controller = new RetainerGuideController(port);
        controller.Start(1, TimeSpan.Zero);
        controller.Update(TimeSpan.Zero);
        controller.Update(TimeSpan.Zero);
        Assert.Equal(RetainerGuideStep.Stopped, controller.Step);
        Assert.Empty(port.Actions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletionWaitsForCleanupAndCanTimeOut(bool timeout)
    {
        var port = new Port { CleanupReady = false };
        var controller = new RetainerGuideController(port);
        controller.Start(1, TimeSpan.Zero);
        for (var tick = 0; tick < 20; tick++) controller.Update(TimeSpan.Zero);
        Assert.Equal(RetainerGuideStep.CloseOverview, controller.Step);
        Assert.True(controller.Active);
        Assert.DoesNotContain("cleanup", port.Actions);
        port.CleanupReady = true;
        controller.Update(timeout ? TimeSpan.FromSeconds(15) : TimeSpan.FromSeconds(1));
        Assert.Equal(timeout ? RetainerGuideStep.Stopped : RetainerGuideStep.Complete, controller.Step);
        Assert.Equal(!timeout, port.Actions.Contains("cleanup"));
    }
    [Fact]
    public void VisitsEachRetainerAndCapturesBeforeClosingWithOneRequestPerTransition()
    {
        var port = new Port(); var controller = new RetainerGuideController(port);
        controller.Start(1, TimeSpan.Zero);
        for (var tick = 0; tick < 20; tick++) controller.Update(TimeSpan.FromMilliseconds(tick));
        Assert.Equal(RetainerGuideStep.Complete, controller.Step);
        Assert.False(controller.Active);
        Assert.Equal("Retainer capture complete (2/2).", controller.Message);
        Assert.Equal(new[] { "select:11", "open:11", "capture:11", "close:11", "quit:11", "select:22", "open:22", "capture:22", "close:22", "quit:22", "cleanup" }, port.Actions);
    }

    [Fact]
    public void PendingInteractionIsNotRetriedAndNoActionOccursAtTheExactTimeout()
    {
        var port = new Port { ListReady = false }; var controller = new RetainerGuideController(port);
        controller.Start(1, TimeSpan.Zero);
        controller.Update(TimeSpan.Zero);
        controller.Update(TimeSpan.FromSeconds(59));
        Assert.Equal(new[] { "bell" }, port.Actions);
        Assert.Equal("Opening Summoning Bell", controller.Message);
        controller.Update(TimeSpan.FromSeconds(60));
        Assert.Equal(RetainerGuideStep.Stopped, controller.Step);
        Assert.Single(port.Actions);
    }

    [Fact]
    public void WaitsForRangeWithoutInputThenAcceptsTheReadyList()
    {
        var port = new Port { ListReady = false, BellReady = false }; var controller = new RetainerGuideController(port);
        controller.Start(1, TimeSpan.Zero); controller.Update(TimeSpan.Zero);
        Assert.Empty(port.Actions); Assert.Equal("Move near a Summoning Bell", controller.Message);
        port.ListReady = true; controller.Update(TimeSpan.FromSeconds(1));
        Assert.Equal(RetainerGuideStep.ReadRoster, controller.Step);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CancellationOrChangedSafetyStopsWithoutClosingOrAdditionalActions(bool cancel)
    {
        var port = new Port(); var controller = new RetainerGuideController(port);
        controller.Start(1, TimeSpan.Zero);
        for (var tick = 0; tick < 4; tick++) controller.Update(TimeSpan.FromMilliseconds(tick));
        Assert.Equal(RetainerGuideStep.CaptureInventory, controller.Step);
        var count = port.Actions.Count;
        if (cancel) controller.Cancel(); else port.Safe = false;
        controller.Update(TimeSpan.FromSeconds(1));
        Assert.Equal(RetainerGuideStep.Stopped, controller.Step); Assert.Equal(count, port.Actions.Count);
    }

    [Fact]
    public void CapturingWaitsForACompleteReadAndCloseIsNeverRequestedForAnIncompleteRead()
    {
        var port = new Port { Capturable = false }; var controller = new RetainerGuideController(port);
        controller.Start(1, TimeSpan.Zero);
        for (var tick = 0; tick < 5; tick++) controller.Update(TimeSpan.Zero);
        Assert.Equal(RetainerGuideStep.CaptureInventory, controller.Step);
        Assert.DoesNotContain(port.Actions, item => item.StartsWith("close:"));
        controller.Update(TimeSpan.FromSeconds(15));
        Assert.Equal(RetainerGuideStep.Stopped, controller.Step);
        Assert.DoesNotContain(port.Actions, item => item.StartsWith("close:"));
    }

    [Fact]
    public void AChangedRosterStopsBeforeSelectingAnyRetainer()
    {
        var port = new Port(); var controller = new RetainerGuideController(port);
        controller.Start(1, TimeSpan.Zero); controller.Update(TimeSpan.Zero); controller.Update(TimeSpan.Zero);
        port.Roster = [new(22, "Second"), new(11, "First")]; controller.Update(TimeSpan.Zero);
        Assert.Equal(RetainerGuideStep.Stopped, controller.Step); Assert.Empty(port.Actions);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void InvalidRosterCannotStartSelection(int kind)
    {
        var port = new Port
        {
            Roster = kind switch
            {
                0 => default,
                1 => [new(0, "Unknown")],
                2 => [new(11, "First"), new(11, "Duplicate")],
                _ => Enumerable.Range(1, 11).Select(id => new InventoryRetainer((ulong)id, "Name")).ToImmutableArray(),
            },
        };
        var controller = new RetainerGuideController(port);
        controller.Start(1, TimeSpan.Zero); controller.Update(TimeSpan.Zero); controller.Update(TimeSpan.Zero);
        Assert.Equal(RetainerGuideStep.Stopped, controller.Step); Assert.Empty(port.Actions);
    }

    [Fact]
    public void EmptyRosterCompletesAndActiveStartDoesNotResetTheRun()
    {
        var port = new Port { Roster = [] }; var controller = new RetainerGuideController(port);
        controller.Cancel(); controller.Update(TimeSpan.Zero); Assert.Equal(RetainerGuideStep.Idle, controller.Step);
        controller.Start(0, TimeSpan.Zero); Assert.False(controller.Active);
        controller.Start(1, TimeSpan.Zero); controller.Update(TimeSpan.Zero);
        controller.Start(2, TimeSpan.FromHours(1)); Assert.Equal(RetainerGuideStep.ReadRoster, controller.Step);
        controller.Update(TimeSpan.Zero); controller.Update(TimeSpan.Zero); Assert.Equal(RetainerGuideStep.Complete, controller.Step);
        Assert.Equal("Retainer capture complete (0/0).", controller.Message);
    }

    [Fact]
    public void MenuAndClosureWaitsNeverRepeatSuccessfulRequests()
    {
        var port = new Port { MenuReady = false }; var controller = new RetainerGuideController(port);
        controller.Start(1, TimeSpan.Zero);
        for (var i = 0; i < 4; i++) controller.Update(TimeSpan.Zero);
        Assert.Equal(RetainerGuideStep.OpenInventory, controller.Step);
        Assert.Equal(new[] { "select:11" }, port.Actions);
        port.MenuReady = true;
        controller.Update(TimeSpan.Zero); controller.Update(TimeSpan.Zero); controller.Update(TimeSpan.Zero);
        port.InventoryVisible = true;
        controller.Update(TimeSpan.Zero); controller.Update(TimeSpan.Zero);
        Assert.Equal(RetainerGuideStep.QuitRetainer, controller.Step);
        Assert.Equal(new[] { "select:11", "open:11", "capture:11", "close:11" }, port.Actions);
        port.InventoryVisible = false; controller.Update(TimeSpan.Zero);
        port.ListReady = false; controller.Update(TimeSpan.Zero); controller.Update(TimeSpan.Zero);
        Assert.Equal(RetainerGuideStep.WaitForList, controller.Step);
        Assert.Single(port.Actions, item => item == "quit:11");
    }

    [Fact]
    public void UnreadyRosterAndRejectedActionsWaitWithoutSkippingAnyStep()
    {
        var port = new Port(); var controller = new RetainerGuideController(port);
        port.Safe = false; controller.Start(1, TimeSpan.Zero); Assert.Equal(RetainerGuideStep.Stopped, controller.Step);
        port.Safe = true; controller.Start(1, TimeSpan.Zero); controller.Update(TimeSpan.Zero);
        port.ListReady = false; controller.Update(TimeSpan.Zero); Assert.Equal(RetainerGuideStep.ReadRoster, controller.Step);
        port.ListReady = true; controller.Update(TimeSpan.Zero);
        port.ListReady = false; controller.Update(TimeSpan.Zero); Assert.Equal(RetainerGuideStep.SelectRetainer, controller.Step);
        port.ListReady = true; port.SelectionReady = false; controller.Update(TimeSpan.Zero);
        Assert.Equal(RetainerGuideStep.SelectRetainer, controller.Step); Assert.Empty(port.Actions);
        port.SelectionReady = true; controller.Update(TimeSpan.Zero); controller.Update(TimeSpan.Zero); controller.Update(TimeSpan.Zero);
        port.CloseReady = false; controller.Update(TimeSpan.Zero); Assert.Equal(RetainerGuideStep.CloseInventory, controller.Step);
        port.CloseReady = true; controller.Update(TimeSpan.Zero);
        port.QuitReady = false; controller.Update(TimeSpan.Zero); Assert.Equal(RetainerGuideStep.QuitRetainer, controller.Step);
        port.QuitReady = true; controller.Update(TimeSpan.Zero); Assert.Equal(RetainerGuideStep.WaitForList, controller.Step);
    }

    private sealed class Port : IRetainerGuidePort
    {
        public bool Safe = true;
        public bool BellReady = true;
        public bool Capturable = true;
        public bool MenuReady = true;
        public bool SelectionReady = true;
        public bool CloseReady = true;
        public bool QuitReady = true;
        public bool CleanupReady = true;
        public ImmutableArray<InventoryRetainer> Roster = [new(11, "First"), new(22, "Second")];
        public List<string> Actions { get; } = [];
        public bool ListReady { get; set; } = true;
        public bool InventoryVisible { get; set; }
        public bool IsSafe(ulong contentId) => Safe && contentId == 1;
        public bool OpenBell() { if (!BellReady) return false; Actions.Add("bell"); return true; }
        public bool TryReadRoster(out ImmutableArray<InventoryRetainer> roster) { roster = Roster; return ListReady; }
        public bool SelectRetainer(ulong id) { if (!SelectionReady) return false; Actions.Add($"select:{id}"); return true; }
        public bool OpenInventory(ulong id) { if (!MenuReady) return false; Actions.Add($"open:{id}"); return true; }
        public bool CaptureInventory(ulong contentId, ulong id) { if (!Capturable) return false; Actions.Add($"capture:{id}"); return true; }
        public bool CloseInventory(ulong id) { if (!CloseReady) return false; Actions.Add($"close:{id}"); return true; }
        public bool CloseOverviewAndInventory() { if (!CleanupReady) return false; Actions.Add("cleanup"); return true; }
        public bool QuitRetainer(ulong id) { if (!QuitReady) return false; Actions.Add($"quit:{id}"); return true; }
    }
}

