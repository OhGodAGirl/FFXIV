using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class InventorySlotPolicyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void ZeroQuantityIsEmptyOnlyForFixedCrystalSlots(bool crystal, bool accepted)
    {
        Assert.Equal(accepted, InventorySlotPolicy.TryCreate(10, 0, false, crystal, out var item));
        Assert.Null(item);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void QuantityIsAssignedToExactlyOneQuality(bool highQuality, bool crystal)
    {
        Assert.True(InventorySlotPolicy.TryCreate(10, uint.MaxValue, highQuality, crystal, out var item));
        Assert.Equal(new InventoryItemQuantity(10, highQuality ? 0 : uint.MaxValue, highQuality ? uint.MaxValue : 0), item);
    }
}
