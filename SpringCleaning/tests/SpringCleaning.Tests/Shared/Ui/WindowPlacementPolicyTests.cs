namespace SpringCleaning.Tests;

public sealed class WindowPlacementPolicyTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void SessionSnapHonorsRememberPlacementSetting(bool rememberPlacement, bool expected)
    {
        // Arrange

        // Act
        var shouldSnap = WindowPlacementPolicy.ShouldSnapAtSessionStart(rememberPlacement);

        // Assert
        Assert.Equal(expected, shouldSnap);
    }

    [Theory]
    [InlineData(360f, 1f, 360f)]
    [InlineData(360f, 1.75f, 630f)]
    public void PanelWidthUsesDalamudUiScale(
        float logicalWidth,
        float globalScale,
        float expected)
    {
        // Arrange

        // Act
        var width = WindowPlacementPolicy.GetScaledPanelWidth(
            logicalWidth,
            globalScale);

        // Assert
        Assert.Equal(expected, width);
    }

    [Theory]
    [InlineData(false, false, false, 100f)]
    [InlineData(true, false, false, 528f)]
    [InlineData(true, true, false, 528f)]
    [InlineData(true, false, true, 1_236f)]
    public void GlamourCreationUsesTheOutermostVisibleSidecar(
        bool diagnosticsOpen,
        bool settingsOpen,
        bool migrationOpen,
        float expected)
    {
        // Arrange
        const float dresserSidecarX = 100f;
        const float gap = 8f;

        // Act
        var x = WindowPlacementPolicy.GetGlamourCreationX(
            dresserSidecarX,
            gap,
            diagnosticsOpen,
            diagnosticWidth: 420f,
            settingsOpen,
            settingsWidth: 360f,
            migrationOpen,
            migrationWidth: 700f);

        // Assert
        Assert.Equal(expected, x);
    }

    [Fact]
    public void SettingsColumnAloneOffsetsTheGlamourCreationWindow()
    {
        var x = WindowPlacementPolicy.GetGlamourCreationX(
            dresserSidecarX: 100f,
            gap: 8f,
            diagnosticsOpen: false,
            diagnosticWidth: 420f,
            settingsOpen: true,
            settingsWidth: 360f,
            migrationOpen: false,
            migrationWidth: 700f);

        Assert.Equal(468f, x);
    }

    [Fact]
    public void InitialSnapSurvivesNativeWindowInitializationThenReleases()
    {
        // Arrange
        var startedAt = new DateTime(2026, 8, 30, 15, 0, 0, DateTimeKind.Utc);

        // Act
        var deadline = WindowPlacementPolicy.GetInitialSnapDeadline(startedAt);
        var duringInitialization = WindowPlacementPolicy.ShouldApplyInitialSnap(
            startedAt.AddSeconds(1),
            deadline);
        var atDeadline = WindowPlacementPolicy.ShouldApplyInitialSnap(deadline, deadline);
        var afterInitialization = WindowPlacementPolicy.ShouldApplyInitialSnap(
            startedAt.AddSeconds(2),
            deadline);

        // Assert
        Assert.True(duringInitialization);
        Assert.True(atDeadline);
        Assert.False(afterInitialization);
    }
}
