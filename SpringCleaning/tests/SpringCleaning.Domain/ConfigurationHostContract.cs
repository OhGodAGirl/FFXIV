// Test-host contract only. Production compiles against Dalamud's actual interface.
namespace Dalamud.Configuration;
internal interface IPluginConfiguration
{
    int Version { get; set; }
}
