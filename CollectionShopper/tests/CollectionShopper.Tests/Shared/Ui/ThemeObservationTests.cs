namespace CollectionShopper.Tests;

public sealed class ThemeObservationTests
{
    [Fact]
    public void SuccessfulReadsApplyEveryThemeChange()
    {
        // Arrange
        uint nextTheme = 0;
        var applied = new List<uint>();
        var observation = new ThemeObservation(() => nextTheme, applied.Add);
        // Act
        foreach (var theme in new uint[] { 0, 7, 2, uint.MaxValue, 0 })
        {
            nextTheme = theme;
            observation.Update();
        }
        // Assert
        Assert.Equal(new uint[] { 0, 7, 2, uint.MaxValue, 0 }, applied);
    }

    [Fact]
    public void MissingAndThrowingReadsRetainLastAppliedThemeAndRecover()
    {
        // Arrange
        uint? nextTheme = 3;
        var throws = false;
        var applied = new List<uint>();
        var observation = new ThemeObservation(() => throws ? throw new InvalidOperationException() : nextTheme, applied.Add);
        observation.Update();
        // Act
        nextTheme = null;
        observation.Update();
        throws = true;
        observation.Update();
        // Assert
        Assert.Equal(new uint[] { 3 }, applied);
        // Act: a later valid read still applies.
        throws = false;
        nextTheme = 4;
        observation.Update();
        // Assert
        Assert.Equal(new uint[] { 3, 4 }, applied);
    }

    [Fact]
    public void ApplyFailureDoesNotEscapeOrPreventLaterObservation()
    {
        // Arrange
        var fail = true;
        var applied = new List<uint>();
        var observation = new ThemeObservation(() => 5, theme =>
        {
            if (fail) throw new InvalidOperationException();
            applied.Add(theme);
        });
        // Act
        var error = Record.Exception(observation.Update);
        fail = false;
        observation.Update();
        // Assert
        Assert.Null(error);
        Assert.Equal(new uint[] { 5 }, applied);
    }
}
