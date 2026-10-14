using System.Reflection;
namespace Exportaramus.Bootstrap;

public static class PluginIdentity
{
    public const string Name = "Exportaramus";
    public const string SchemaVersion = "1.0";
    public static string Version { get; } = typeof(PluginIdentity).Assembly.GetName().Version!.ToString();
}
