using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class InventoryContainerReadTests
{
    [Fact]
    public void CompleteReadCopiesNonemptySlotsAndChecksTheEntireGroup()
    {
        var source = new Source();
        var result = InventoryContainerReadPolicy.Read(source, [1, 2]);
        Assert.Equal([new InventorySlotObservation(1, 0, 100, 5, true), new(2, 0, 100, 5, true)], result);
        Assert.Equal(4, source.ContainerCalls);
        Assert.Equal(2, source.KnownCalls);
        Assert.Empty(InventoryContainerReadPolicy.Read(source, [])!);
    }
    [Theory]
    [InlineData(0, 2, true)]
    [InlineData(1, 2, false)]
    [InlineData(1, 0, true)]
    [InlineData(1, -1, true)]
    [InlineData(1, 1001, true)]
    public void InvalidContainersNeverPublish(int address, int size, bool loaded)
    {
        var source = new Source { ContainerValue = new(address, size, loaded) };
        Assert.Null(InventoryContainerReadPolicy.Read(source, [1]));
    }
    [Fact]
    public void MaximumValidSizeIsReadable()
    {
        var source = new Source { ContainerValue = new(1, 1000, true) };
        Assert.Single(InventoryContainerReadPolicy.Read(source, [1])!);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MissingMismatchedOrUnknownSlotsNeverPublish(int failure)
    {
        var source = new Source { SlotFailure = failure };
        Assert.Null(InventoryContainerReadPolicy.Read(source, [1]));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrReplacedManagerNeverPublishes(bool afterRead)
    {
        var source = new Source { ManagerFailureAt = afterRead ? 2 : 1 };
        Assert.Null(InventoryContainerReadPolicy.Read(source, [1]));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void LaterPageInvalidatingEarlierPageRejectsTheWholeRead(int change)
    {
        var source = new Source { ChangeAfterRead = change };
        Assert.Null(InventoryContainerReadPolicy.Read(source, [1, 2]));
    }
    [Fact]
    public void ProjectionsPreservePresenceInstancesQuantitiesAndQuality()
    {
        InventorySlotObservation[] slots = [new(1, 0, 100, 5, true), new(1, 1, 100, 2, false)];
        Assert.Equal([new InventoryItemQuantity(100, 2, 5)], InventoryContainerProjection.Quantities(slots, 8, 9));
        Assert.Equal(new uint?[] { 5, 2 }, InventoryContainerProjection.Presence(slots, false).Select(item => item.Quantity));
        Assert.Equal(new uint?[] { 1, 1 }, InventoryContainerProjection.Presence(slots, true).Select(item => item.Quantity));
        Assert.Null(InventoryContainerProjection.Quantities([new(1, 0, 100, 0, false)], 8, 9));
        Assert.Empty(InventoryContainerProjection.Quantities([new(8, 0, 100, 0, false), new(9, 0, 100, 0, false)], 8, 9)!);
        Assert.Null(InventoryContainerProjection.Quantities([new(1, 0, 0, 1, false)], 8, 9));
    }
    private sealed class Source : IInventoryContainerSource
    {
        public InventoryContainerObservation ContainerValue = new(1, 2, true);
        public int SlotFailure = -1;
        public int ChangeAfterRead = -1;
        public int ManagerFailureAt = -1;
        public int ContainerCalls;
        public int KnownCalls;
        private int managerCalls;
        public bool IsCurrent => ++managerCalls != ManagerFailureAt;
        public InventoryContainerObservation Container(int type)
        {
            ContainerCalls++;
            if (ContainerCalls <= 2) return ContainerValue;
            return ChangeAfterRead switch
            {
                0 => ContainerValue with { Address = 2 },
                1 => ContainerValue with { Size = 3 },
                2 => ContainerValue with { Loaded = false },
                _ => ContainerValue,
            };
        }
        public InventorySlotObservation? Slot(int type, int slot) => SlotFailure switch
        {
            0 => null,
            1 => new(99, slot, 100, 5, false),
            2 => new(type, 99, 100, 5, false),
            _ => new(type, slot, slot == 0 ? 100u : 0, 5, true),
        };
        public bool IsKnownItem(uint id)
        {
            KnownCalls++; return SlotFailure != 3;
        }
    }
}
