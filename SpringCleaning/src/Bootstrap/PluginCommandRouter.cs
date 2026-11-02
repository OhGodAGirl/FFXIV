namespace SpringCleaning;

/// <summary>Routes the normal plugin command separately from opt-in support tracing.</summary>
internal static class PluginCommandRouter
{
    internal static PluginCommandDestination Route(string? arguments) =>
        string.Equals(arguments?.Trim(), "diagnostics", StringComparison.OrdinalIgnoreCase)
            ? PluginCommandDestination.Diagnostics
            : PluginCommandDestination.None;
}
