namespace SpringCleaning.Tests;

public sealed class ExistingOutfitConfirmationPolicyTests
{
    [Theory]
    [InlineData("An outfit glamour matching this gear is available.\r\nAdd to pre-existing outfit glamour?")]
    [InlineData("選択した装備品を収納可能な装備一式があります。\r\n既に幻影化されている装備一式に収納しますか？")]
    [InlineData("Es gibt eine passende Setprojektion für diese Ausrüstung.\r\nZum vorhandenen Set hinzufügen?")]
    [InlineData("Cet objet appartient à un ensemble complet existant.\r\nRanger et ajouter au mirage d'ensemble\u00a0?")]
    public void LocalizedPromptAllowsOnlyWhitespaceRenderingDifferences(string expected)
    {
        var rendered = "  " + expected.Replace("\r\n", "\n").Replace('\u00a0', ' ') + "\t";
        Assert.True(ExistingOutfitConfirmationPolicy.Matches(rendered, expected));
        Assert.False(ExistingOutfitConfirmationPolicy.Matches("Discard this item?", expected));
        Assert.False(ExistingOutfitConfirmationPolicy.Matches(expected + " Unrelated action", expected));
        Assert.False(ExistingOutfitConfirmationPolicy.Matches(expected[..^1], expected));
    }

    [Theory]
    [InlineData(null, "Expected")]
    [InlineData("", "Expected")]
    [InlineData(" \t", "Expected")]
    [InlineData("Expected", null)]
    [InlineData("Expected", "")]
    [InlineData("Expected", "\r\n")]
    [InlineData("expected", "Expected")]
    public void MissingOrDifferentPromptCannotAuthorizeYes(string? prompt, string? expected)
    {
        Assert.False(ExistingOutfitConfirmationPolicy.Matches(prompt, expected));
    }
}
