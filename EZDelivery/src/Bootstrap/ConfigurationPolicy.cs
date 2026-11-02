using EZDelivery.Features.Interface;

namespace EZDelivery.Bootstrap;

/// <summary>Mutable presentation settings used by the pure configuration policy.</summary>
public interface IConfigurationSettings
{
    /// <summary>Persisted reduced-motion preference.</summary>
    bool ReduceMotion { get; set; }
    /// <summary>Persisted tab-shape preference.</summary>
    ShopTabStyle ShopTabStyle { get; set; }
}

/// <summary>Validation and persistence rules for presentation preferences.</summary>
public static class ConfigurationPolicy
{
    /// <summary>Configuration schema understood by this build.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Rejects every configuration schema other than the current supported version.</summary>
    public static void ValidateVersion(int version)
    {
        if (version != CurrentVersion)
        {
            throw new InvalidOperationException("Unsupported EZ Delivery configuration version; original configuration was preserved.");
        }
    }

    /// <summary>Applies a current-generation setting intent and reports persistence failure.</summary>
    public static bool TryApply(IConfigurationSettings settings, UiIntent intent, long generation,
        Action save, out Exception? saveError)
    {
        saveError = null;
        if (intent.Generation != generation)
        {
            return false;
        }

        switch (intent)
        {
            case ReduceMotionIntent motion:
                settings.ReduceMotion = motion.Enabled;
                break;
            case ShopTabStyleIntent tabs when TabPresentation.IsSupported(tabs.Style):
                settings.ShopTabStyle = tabs.Style;
                break;
            default:
                return false;
        }

        try
        {
            save();
        }
        catch (Exception error)
        {
            // The current session keeps the requested setting when persistence fails.
            saveError = error;
        }

        return true;
    }
}
