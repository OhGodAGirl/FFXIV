namespace CollectionShopper.Tests;

/// <summary>
/// Verifies the official product identity and the legacy command alias.
/// </summary>
public sealed class PluginIdentityTests
{
    /// <summary>
    /// Verifies the slash command follows the one-word lowercase convention.
    /// </summary>
    [Fact]
    public void CommandUsesOneLowercaseWord()
    {
        // Arrange

        // Act
        var command = PluginIdentity.CommandName;

        // Assert
        Assert.Matches("^/[a-z][a-z0-9]*$", command);
    }

    [Fact]
    public void OfficialRenameUsesCollectionShopperAndKeepsLegacyCommand()
    {
        Assert.Equal("Collection Shopper", PluginIdentity.PublicName);
        Assert.Equal("CollectionShopper", PluginIdentity.InternalName);
        Assert.Equal("/collectionshopper", PluginIdentity.CommandName);
        Assert.Equal("/glamshop", PluginIdentity.LegacyCommandName);
    }

    /// <summary>
    /// Verifies the visible window title retains a stable internal ImGui ID.
    /// </summary>
    [Fact]
    public void WindowIdKeepsPublicAndInternalIdentity()
    {
        // Arrange
        var expected = $"{PluginIdentity.PublicName}##{PluginIdentity.InternalName}";

        // Act
        var windowId = PluginIdentity.WindowId;

        // Assert
        Assert.Equal(
            expected,
            windowId);
    }
}
