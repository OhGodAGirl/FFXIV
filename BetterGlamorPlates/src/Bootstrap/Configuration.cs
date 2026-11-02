using BetterGlamorPlates.Features.GlamourPlates;
using Dalamud.Configuration;

namespace BetterGlamorPlates.Bootstrap;

/// <summary>
/// Stores the versioned local configuration and unlimited saved plate library.
/// </summary>
public sealed class Configuration : IPluginConfiguration
{
    /// <summary>
    /// Gets or sets the persisted configuration schema version.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Gets or sets the locally saved plate configurations.
    /// </summary>
    public List<SavedPlate> SavedPlates { get; set; } = [];

    /// <summary>
    /// Gets or sets the most recently edited saved plate.
    /// </summary>
    public Guid? SelectedPlateId { get; set; }
}
