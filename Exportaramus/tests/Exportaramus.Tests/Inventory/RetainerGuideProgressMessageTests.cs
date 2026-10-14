using System.Collections.Immutable;
using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class RetainerGuideProgressMessageTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AnInvalidRosterExplainsWhyCaptureStoppedWithoutSelectingAnyone(int defect)
    {
        var port = new ProgressPort
        {
            Roster = defect switch
            {
                0 => default,
                1 => Enumerable.Range(1, 11).Select(id => new InventoryRetainer((ulong)id, "Name")).ToImmutableArray(),
                2 => [new(0, "Unknown")],
                _ => [new(11, "First"), new(11, "Duplicate")],
            },
        };
        var guide = new RetainerGuideController(port);
        guide.Start(1, TimeSpan.Zero);
        guide.Update(TimeSpan.Zero);
        guide.Update(TimeSpan.Zero);

        Assert.Equal(RetainerGuideStep.Stopped, guide.Step);
        Assert.False(guide.Active);
        Assert.Equal("The Retainer list could not be read completely.", guide.Message);
        Assert.Empty(port.Actions);
        guide.Update(TimeSpan.FromSeconds(1));
        Assert.Equal("The Retainer list could not be read completely.", guide.Message);
        Assert.Empty(port.Actions);
    }

    [Fact]
    public void TheLastCapturedRetainerExplainsPendingCleanupBeforeReportingCompletion()
    {
        var port = new ProgressPort();
        var guide = new RetainerGuideController(port);
        guide.Start(1, TimeSpan.Zero);
        for (var tick = 0; tick < 8; tick++)
            guide.Update(TimeSpan.FromMilliseconds(tick));

        var capturedActions = new[] { "select:11", "open:11", "capture:1:11", "close:11", "quit:11" };
        Assert.Equal(capturedActions, port.Actions);
        Assert.Equal(RetainerGuideStep.CloseOverview, guide.Step);
        Assert.True(guide.Active);
        Assert.Equal("Closing Retainer windows", guide.Message);
        Assert.Equal(0, port.CleanupRequests);

        guide.Update(TimeSpan.FromSeconds(1));
        Assert.Equal(1, port.CleanupRequests);
        Assert.Equal(capturedActions, port.Actions);
        Assert.Equal(RetainerGuideStep.CloseOverview, guide.Step);
        Assert.True(guide.Active);
        Assert.Equal("Closing Retainer windows", guide.Message);

        port.CleanupReady = true;
        guide.Update(TimeSpan.FromSeconds(2));
        Assert.Equal(2, port.CleanupRequests);
        Assert.Equal(capturedActions, port.Actions);
        Assert.Equal(RetainerGuideStep.Complete, guide.Step);
        Assert.False(guide.Active);
        Assert.Equal("Retainer capture complete (1/1).", guide.Message);
    }

    private sealed class ProgressPort : IRetainerGuidePort
    {
        public ImmutableArray<InventoryRetainer> Roster { get; init; } = [new(11, "First")];
        public List<string> Actions { get; } = [];
        public bool CleanupReady { get; set; }
        public int CleanupRequests { get; private set; }
        public bool ListReady => true;
        public bool InventoryVisible => false;
        public bool IsSafe(ulong contentId) => contentId == 1;
        public bool OpenBell() => throw new InvalidOperationException("The roster is already visible.");
        public bool TryReadRoster(out ImmutableArray<InventoryRetainer> roster) { roster = Roster; return true; }
        public bool SelectRetainer(ulong id) { Actions.Add($"select:{id}"); return true; }
        public bool OpenInventory(ulong id) { Actions.Add($"open:{id}"); return true; }
        public bool CaptureInventory(ulong contentId, ulong id) { Actions.Add($"capture:{contentId}:{id}"); return true; }
        public bool CloseInventory(ulong id) { Actions.Add($"close:{id}"); return true; }
        public bool QuitRetainer(ulong id) { Actions.Add($"quit:{id}"); return true; }
        public bool CloseOverviewAndInventory() { CleanupRequests++; return CleanupReady; }
    }
}
