namespace BetterGlamorPlates.Tests;

using BetterGlamorPlates.Bootstrap;

/// <summary>
/// Verifies the public and technical plugin identities remain aligned.
/// </summary>
public sealed class PluginIdentityTests
{
    /// <summary>
    /// Verifies the slash command follows the one-word lowercase convention.
    /// </summary>
    [Fact]
    public void CommandUsesOneLowercaseWord()
    {
        Assert.Matches("^/[a-z][a-z0-9]*$", PluginIdentity.CommandName);
    }

    /// <summary>
    /// Verifies the visible window title retains a stable internal ImGui ID.
    /// </summary>
    [Fact]
    public void WindowIdKeepsPublicAndInternalIdentity()
    {
        Assert.Equal(
            $"{PluginIdentity.PublicName}##{PluginIdentity.InternalName}",
            PluginIdentity.WindowId);
    }
}
