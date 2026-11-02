using Dalamud.Configuration;

namespace EZDelivery.Bootstrap;

/// <summary>Persisted presentation preferences with an explicit migration version.</summary>
[Serializable]
public sealed class Configuration : IPluginConfiguration, IConfigurationSettings
{
    /// <summary>Persisted schema version checked before configuration use.</summary>
    public int Version { get; set; } = ConfigurationPolicy.CurrentVersion;
    /// <summary>Whether the player requested reduced motion.</summary>
    public bool ReduceMotion { get; set; }
    /// <summary>Shape shared by the delivery and shopping tabs.</summary>
    public Features.Interface.ShopTabStyle ShopTabStyle { get; set; } = Features.Interface.ShopTabStyle.InsetSockets;
}
