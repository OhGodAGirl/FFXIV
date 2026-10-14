namespace Exportaramus.Bootstrap;

public static class ConfigurationPolicy
{
    public static bool RestoreReduceMotion(int version, bool storedValue) => version == 1 && storedValue;
}
