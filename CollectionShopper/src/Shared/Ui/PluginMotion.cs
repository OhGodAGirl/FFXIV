namespace CollectionShopper;

internal static class PluginMotion
{
    internal const float FastSeconds = 0.120f;
    internal const float StandardSeconds = 0.180f;
    internal const float EmphasisSeconds = 0.220f;
    internal const float WorkingPulseSeconds = 0.900f;
    internal const double WorkingClockSeconds = 1.2d;
    internal const float MaximumTranslation = 6f;
    internal const float PressOffset = 1.5f;

    internal static float WorkingClockAngle(double elapsedSeconds, bool reduceMotion)
        => reduceMotion
            ? -MathF.PI / 2f
            : (float)((elapsedSeconds % WorkingClockSeconds) / WorkingClockSeconds * Math.Tau) - MathF.PI / 2f;

    internal static float Progress(float elapsedSeconds, float durationSeconds, bool reduceMotion)
    {
        if (reduceMotion || durationSeconds <= 0f)
        {
            return 1f;
        }

        return Math.Clamp(elapsedSeconds / durationSeconds, 0f, 1f);
    }

    internal static float SmoothStep(float progress)
    {
        var value = Math.Clamp(progress, 0f, 1f);
        return value * value * (3f - (2f * value));
    }

    internal static float EaseOutCubic(float progress)
    {
        var value = 1f - Math.Clamp(progress, 0f, 1f);
        return 1f - (value * value * value);
    }

    internal static float WorkingPulse(float elapsedSeconds, bool reduceMotion)
    {
        if (reduceMotion)
        {
            return 0.5f;
        }

        var cycle = PositiveModulo(elapsedSeconds, WorkingPulseSeconds) / WorkingPulseSeconds;
        return 1f - MathF.Abs((cycle * 2f) - 1f);
    }

    private static float PositiveModulo(float value, float modulus)
    {
        return value - (MathF.Floor(value / modulus) * modulus);
    }

}
