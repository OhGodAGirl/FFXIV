namespace SpringCleaning.Tests;

public sealed class NativeSafetyPoliciesTests
{
    [Fact]
    public void StationIdentityMatchesStableIdWhenDisplayNameIsLocalized()
    {
        // Arrange
        IReadOnlySet<uint> stableIds = new HashSet<uint> { 42 };

        // Act
        var matches = StationIdentityPolicy.Matches(
            stableIds,
            baseId: 42,
            displayedName: "Coiffeuse mirage",
            englishFallbackName: "Glamour Dresser");

        // Assert
        Assert.True(matches);
    }

    [Fact]
    public void StationIdentityKeepsEnglishNameAsCompatibilityFallback()
    {
        // Arrange
        IReadOnlySet<uint> stableIds = new HashSet<uint>();

        // Act
        var matches = StationIdentityPolicy.Matches(
            stableIds,
            baseId: 99,
            displayedName: "Glamour Dresser",
            englishFallbackName: "Glamour Dresser");

        // Assert
        Assert.True(matches);
    }

    [Fact]
    public void StationIdentityRejectsUnrelatedObject()
    {
        // Arrange
        IReadOnlySet<uint> stableIds = new HashSet<uint> { 42 };

        // Act
        var matches = StationIdentityPolicy.Matches(
            stableIds,
            baseId: 99,
            displayedName: "Summoning Bell",
            englishFallbackName: "Glamour Dresser");

        // Assert
        Assert.False(matches);
    }

    [Theory]
    [InlineData("Discard Sample Hat?", "Sample Hat", true)]
    [InlineData("Discard SAMPLE HAT?", "Sample Hat", true)]
    [InlineData("Discard the pair of templar’s vambraces?", "Templar’s Vambraces", true)]
    [InlineData("Discard Sample Boots?", "Sample Hat", false)]
    [InlineData("", "Sample Hat", false)]
    [InlineData("Discard Sample Hat?", "", false)]
    public void DiscardConfirmationRequiresExpectedItemName(
        string prompt,
        string itemName,
        bool expected)
    {
        // Arrange

        // Act
        var matches = DiscardConfirmationPolicy.MatchesExpectedItem(prompt, itemName);

        // Assert
        Assert.Equal(expected, matches);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void DiscardPromptControlAcceptsOnlyPromptsOwnedByAnIssuedRequest(
        bool requestIssued,
        bool expected)
    {
        // Arrange

        // Act
        var canControl = DiscardConfirmationPolicy.CanControlOwnedPrompt(requestIssued);

        // Assert
        Assert.Equal(expected, canControl);
    }

    [Theory]
    [InlineData(11, 11, 11)]
    [InlineData(11, 9, 9)]
    [InlineData(3, 11, 3)]
    public void OutfitPieceSelectionUsesTheFullNativeCapacity(
        int nativePieceCount,
        int nativeItemCapacity,
        int expected)
    {
        var count = OutfitSelectionBoundsPolicy.GetSelectablePieceCount(
            nativePieceCount,
            nativeItemCapacity);

        Assert.Equal(expected, count);
    }

    [Theory]
    [InlineData(5, 5, 5)]
    [InlineData(7, 5, 5)]
    [InlineData(3, 5, 3)]
    public void OutfitChoiceSelectionHonorsTheVisibleNativeTabs(
        int nativeChoiceCount,
        int tabButtonCount,
        int expected)
    {
        var count = OutfitSelectionBoundsPolicy.GetVisibleOutfitChoiceCount(
            nativeChoiceCount,
            tabButtonCount);

        Assert.Equal(expected, count);
    }

    [Fact]
    public void OutfitSelectionRejectsEveryNegativeNativeBound()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OutfitSelectionBoundsPolicy.GetVisibleOutfitChoiceCount(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => OutfitSelectionBoundsPolicy.GetVisibleOutfitChoiceCount(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => OutfitSelectionBoundsPolicy.GetSelectablePieceCount(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => OutfitSelectionBoundsPolicy.GetSelectablePieceCount(0, -1));
    }
}
