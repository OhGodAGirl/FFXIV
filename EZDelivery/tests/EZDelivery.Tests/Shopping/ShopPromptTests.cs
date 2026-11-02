using EZDelivery.Features.Shopping;
using Xunit;

namespace EZDelivery.Tests.Shopping;

public sealed class ShopPromptTests
{
    private const string Expected = "You cannot currently equip this item.\r\nProceed with the transaction?";

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData(" ")]
    [InlineData("\u00a0\n ")]
    public void LineFormattingDoesNotChangePromptMeaning(string separator)
        => Assert.True(ShopPrompt.Matches("  You cannot currently equip this item." + separator + "Proceed with the transaction?  ", Expected));

    [Theory]
    [InlineData("")]
    [InlineData("Proceed with the transaction?")]
    [InlineData("You cannot currently equip this item. Proceed with the transaction? Extra warning.")]
    [InlineData("You can currently equip this item. Proceed with the transaction?")]
    public void ChangedOrPartialWordingDoesNotMatch(string observed)
        => Assert.False(ShopPrompt.Matches(observed, Expected));

    [Fact]
    public void MissingExpectedPromptCannotAuthorizeConfirmation()
        => Assert.False(ShopPrompt.Matches("", ""));
}
