using EZDelivery.Features.Shopping;
using Xunit;

namespace EZDelivery.Tests.Shopping;

public sealed class ShopRankIconTests
{
    // Supported executable's variant-2 table; Serpents rank 8 is also the reported live failure.
    [Theory]
    [InlineData(1, 8, 83608u)]
    [InlineData(2, 8, 83658u)]
    [InlineData(3, 8, 83708u)]
    [InlineData(2, 1, 83651u)]
    [InlineData(2, 11, 83661u)]
    [InlineData(0, 8, 0u)]
    [InlineData(4, 8, 0u)]
    public void UsesExchangeVariantAndCompany(byte company, byte rank, uint expected)
        => Assert.Equal(expected, ShopRankIcon.Expected(company, rank));
}
