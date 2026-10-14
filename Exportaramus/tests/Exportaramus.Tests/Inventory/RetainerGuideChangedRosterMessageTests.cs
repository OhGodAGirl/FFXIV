using System.Collections.Immutable;
using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class RetainerGuideChangedRosterMessageTests
{
    [Fact]
    public void AChangedRosterExplainsWhyCaptureStoppedBeforeAnySelection()
    {
        var port = new ChangedRosterPort();
        var guide = new RetainerGuideController(port);
        guide.Start(1, TimeSpan.Zero);
        guide.Update(TimeSpan.Zero);
        guide.Update(TimeSpan.Zero);
        Assert.Equal(RetainerGuideStep.SelectRetainer, guide.Step);

        port.Roster = [new(22, "Second"), new(11, "First")];
        guide.Update(TimeSpan.Zero);

        Assert.Equal(RetainerGuideStep.Stopped, guide.Step);
        Assert.Equal("The Retainer list changed. Captured inventories are saved.", guide.Message);
        Assert.False(guide.Active);
        Assert.Empty(port.Actions);
        guide.Update(TimeSpan.FromSeconds(1));
        Assert.Equal("The Retainer list changed. Captured inventories are saved.", guide.Message);
        Assert.Empty(port.Actions);
    }

    private sealed class ChangedRosterPort : IRetainerGuidePort
    {
        public ImmutableArray<InventoryRetainer> Roster { get; set; } = [new(11, "First"), new(22, "Second")];
        public List<string> Actions { get; } = [];
        public bool ListReady => true;
        public bool InventoryVisible => false;
        public bool IsSafe(ulong contentId) => contentId == 1;
        public bool TryReadRoster(out ImmutableArray<InventoryRetainer> roster) { roster = Roster; return true; }
        public bool OpenBell() => Record("OpenBell");
        public bool SelectRetainer(ulong retainerId) => Record("SelectRetainer");
        public bool OpenInventory(ulong retainerId) => Record("OpenInventory");
        public bool CaptureInventory(ulong contentId, ulong retainerId) => Record("CaptureInventory");
        public bool CloseInventory(ulong retainerId) => Record("CloseInventory");
        public bool QuitRetainer(ulong retainerId) => Record("QuitRetainer");
        public bool CloseOverviewAndInventory() => Record("CloseOverviewAndInventory");
        private bool Record(string action) { Actions.Add(action); return false; }
    }
}
