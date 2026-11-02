namespace SpringCleaning.Tests;

/// <summary>Verifies diagnostics remain an explicit support-only command path.</summary>
public sealed class PluginCommandRouterTests
{
    /// <summary>Verifies the support argument is case-insensitive and whitespace tolerant.</summary>
    [Theory]
    [InlineData("diagnostics")]
    [InlineData(" DIAGNOSTICS ")]
    public void DiagnosticsArgumentOpensSupportTracing(string arguments)
    {
        Assert.Equal(PluginCommandDestination.Diagnostics, PluginCommandRouter.Route(arguments));
    }

    /// <summary>Verifies ordinary and unknown arguments retain the main feature behavior.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("open")]
    public void OtherArgumentsDoNotOpenAWindow(string? arguments)
    {
        Assert.Equal(PluginCommandDestination.None, PluginCommandRouter.Route(arguments));
    }
}
