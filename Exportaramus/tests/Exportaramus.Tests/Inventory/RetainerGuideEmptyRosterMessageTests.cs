using System.Collections.Immutable;
using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class RetainerGuideEmptyRosterMessageTests
{
    [Fact]
    public void AnObservedEmptyRosterExplainsThePendingWindowCleanup()
    {
        var port = new EmptyRosterPort();
        var guide = new RetainerGuideController(port);
        guide.Start(1, TimeSpan.Zero);
        guide.Update(TimeSpan.Zero);
        guide.Update(TimeSpan.Zero);

        Assert.Equal(RetainerGuideStep.CloseOverview, guide.Step);
        Assert.Equal("Closing Retainer windows", guide.Message);
        Assert.True(guide.Active);
        Assert.Equal(0, port.CloseRequests);
        guide.Update(TimeSpan.Zero);
        Assert.Equal(1, port.CloseRequests);
        Assert.Equal("Closing Retainer windows", guide.Message);
        Assert.True(guide.Active);
    }

    private sealed class EmptyRosterPort : IRetainerGuidePort
    {
        public int CloseRequests { get; private set; }
        public bool ListReady => true;
        public bool InventoryVisible => false;
        public bool IsSafe(ulong contentId) => contentId == 1;
        public bool OpenBell() => throw new InvalidOperationException("The roster is already visible.");
        public bool TryReadRoster(out ImmutableArray<InventoryRetainer> roster) { roster = []; return true; }
        public bool SelectRetainer(ulong retainerId) => throw new InvalidOperationException("The roster is empty.");
        public bool OpenInventory(ulong retainerId) => throw new InvalidOperationException("The roster is empty.");
        public bool CaptureInventory(ulong contentId, ulong retainerId) => throw new InvalidOperationException("The roster is empty.");
        public bool CloseInventory(ulong retainerId) => throw new InvalidOperationException("The roster is empty.");
        public bool QuitRetainer(ulong retainerId) => throw new InvalidOperationException("The roster is empty.");
        public bool CloseOverviewAndInventory() { CloseRequests++; return false; }
    }
}
