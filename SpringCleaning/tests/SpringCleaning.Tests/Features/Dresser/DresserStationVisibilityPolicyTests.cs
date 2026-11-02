namespace SpringCleaning.Tests;

public sealed class DresserStationVisibilityPolicyTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void OnlyTheStandaloneDresserViewIsOpen(
        bool dresserAddonVisible,
        bool glamourPlateEditorVisible,
        bool expected)
    {
        // Act
        var actual = DresserStationVisibilityPolicy.ShouldShowCompanions(
            dresserAddonVisible,
            glamourPlateEditorVisible);

        // Assert
        Assert.Equal(expected, actual);
    }
}
