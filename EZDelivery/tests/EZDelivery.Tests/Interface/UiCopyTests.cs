using System.Globalization;
using EZDelivery.Features.Interface;

namespace EZDelivery.Tests.Interface;

public sealed class UiCopyTests
{
    [Theory]
    [InlineData("en-US", "12,345")]
    [InlineData("de-DE", "12.345")]
    public void BuyTooltipNamesExactItemQuantityAndCultureFormattedPrice(string culture, string formatted)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var row = new ShopRow("offer", 1, 2, "Serpent Captain's Longsword", ShopCategory.Weapons, 12345, true, "");
            Assert.Equal($"Buy one Serpent Captain's Longsword for {formatted} seals.", UiCopy.Buy(row));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void StopAndBuyAllCopyDescribeTheTransactionBoundaries()
    {
        Assert.Contains("Any action already sent is checked before finishing", UiCopy.Stop);
        Assert.Contains("one of each missing item shown", UiCopy.BuyAll);
        Assert.Contains("until the next purchase is unaffordable", UiCopy.BuyAll);
    }
}
