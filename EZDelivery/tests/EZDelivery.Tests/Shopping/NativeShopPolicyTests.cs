using EZDelivery.Features.Shopping;

namespace EZDelivery.Tests.Shopping;

public sealed class NativeShopPolicyTests
{
    [Theory]
    [InlineData(0ul)] [InlineData(3ul)] [InlineData(4097ul)] [InlineData(8192ul)] [InlineData(ulong.MaxValue)]
    public void InvalidRingSizesRejectIndexing(ulong capacity)
    {
        // Arrange
        const ulong index = 19;
        // Act
        var accepted = NativeShopPolicy.TryConditionIndex(capacity, index, out var slot);
        // Assert
        Assert.False(accepted); Assert.Equal(0ul, slot);
    }

    [Fact]
    public void EverySupportedRingSizeWrapsAtBoundaryAndMaximumIndex()
    {
        // Arrange
        for (ulong capacity = 1; capacity <= 4096; capacity *= 2)
        {
            foreach (var index in new[] { 0ul, capacity - 1, capacity, capacity + 1, ulong.MaxValue })
            {
                // Act
                var accepted = NativeShopPolicy.TryConditionIndex(capacity, index, out var slot);
                // Assert
                Assert.True(accepted); Assert.Equal(index % capacity, slot); Assert.True(slot < capacity);
            }
        }
    }

    [Theory]
    [InlineData(0u, 0u, true)]
    [InlineData(42u, 0u, true)]
    [InlineData(42u, 98u, true)]
    [InlineData(42u, 99u, false)]
    [InlineData(42u, uint.MaxValue, false)]
    [InlineData(43u, 0u, false)]
    public void OnePurchaseFitsOnlyEmptyOrMatchingNonfullStack(uint item, uint quantity, bool expected)
    {
        // Arrange
        const uint requested = 42, stack = 99;
        // Act
        var actual = NativeShopPolicy.SlotAccepts(item, quantity, requested, stack);
        // Assert
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(-1, false)] [InlineData(0, true)] [InlineData(500, true)] [InlineData(501, false)]
    public void InventoryContainerBounds(int size, bool expected)
    {
        // Arrange / Act
        var actual = NativeShopPolicy.ContainerSizeValid(size);
        // Assert
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0L, 0)] [InlineData(2147483647L, int.MaxValue)] [InlineData(2147483648L, -1)]
    public void QuantityOverflowRemainsUnknown(long quantity, int expected)
    {
        // Arrange / Act
        var actual = NativeShopPolicy.BoundedCount(quantity);
        // Assert
        Assert.Equal(expected, actual);
    }
}
