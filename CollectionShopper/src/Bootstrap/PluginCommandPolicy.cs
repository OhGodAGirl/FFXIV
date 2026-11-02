namespace CollectionShopper;

internal enum PluginCommandAction
{
    Toggle,
    Motion,
    Diagnostics,
}

internal static class PluginCommandPolicy
{
    internal static PluginCommandAction Resolve(string arguments)
    {
        var command = arguments.Trim();
        if (command.Equals("motion", StringComparison.OrdinalIgnoreCase)) return PluginCommandAction.Motion;
        if (command.Equals("diagnostics", StringComparison.OrdinalIgnoreCase)) return PluginCommandAction.Diagnostics;
        return PluginCommandAction.Toggle;
    }
}
