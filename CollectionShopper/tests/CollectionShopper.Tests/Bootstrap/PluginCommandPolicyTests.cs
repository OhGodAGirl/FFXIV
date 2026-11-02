namespace CollectionShopper.Tests.Bootstrap;

public sealed class PluginCommandPolicyTests
{
    [Theory]
    [InlineData("motion", (int)PluginCommandAction.Motion)]
    [InlineData("MoTiOn", (int)PluginCommandAction.Motion)]
    [InlineData(" \tMoTiOn\r\n", (int)PluginCommandAction.Motion)]
    [InlineData("diagnostics", (int)PluginCommandAction.Diagnostics)]
    [InlineData("DIAGNOSTICS", (int)PluginCommandAction.Diagnostics)]
    [InlineData("\u2003diagnostics\u2003", (int)PluginCommandAction.Diagnostics)]
    [InlineData("", (int)PluginCommandAction.Toggle)]
    [InlineData(" \t\r\n", (int)PluginCommandAction.Toggle)]
    [InlineData("unknown", (int)PluginCommandAction.Toggle)]
    [InlineData("motion start", (int)PluginCommandAction.Toggle)]
    [InlineData("diagnostics extra", (int)PluginCommandAction.Toggle)]
    [InlineData("diagnostic", (int)PluginCommandAction.Toggle)]
    [InlineData("motions", (int)PluginCommandAction.Toggle)]
    public void CommandRoutingRequiresAnExactTrimmedCaseInsensitiveSubcommand(string arguments, int expected)
    {
        // Arrange
        var input = arguments;

        // Act
        var actual = PluginCommandPolicy.Resolve(input);

        // Assert
        Assert.Equal((PluginCommandAction)expected, actual);
    }
}

