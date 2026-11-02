namespace BetterGlamorPlates.Tests;


/// <summary>
/// Verifies the shared motion tokens remain bounded and accessible.
/// </summary>
public sealed class PluginMotionTests
{
    /// <summary>
    /// Verifies progress clamps invalid boundaries and snaps under reduced motion.
    /// </summary>
    [Theory]
    [InlineData(-1f, 1f, false, 0f)]
    [InlineData(0.5f, 1f, false, 0.5f)]
    [InlineData(0.25f, 0.5f, false, 0.5f)]
    [InlineData(2f, 1f, false, 1f)]
    [InlineData(0f, 0f, false, 1f)]
    [InlineData(0f, -1f, false, 1f)]
    [InlineData(0f, 1f, true, 1f)]
    public void ProgressClampsAndHonorsReducedMotion(
        float elapsed,
        float duration,
        bool reduceMotion,
        float expected)
    {
        Assert.Equal(expected, PluginMotion.Progress(elapsed, duration, reduceMotion), 5);
    }

    /// <summary>
    /// Verifies smoothstep clamps and preserves its midpoint.
    /// </summary>
    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(1f, 1f)]
    [InlineData(2f, 1f)]
    public void SmoothStepIsBounded(float progress, float expected)
    {
        Assert.Equal(expected, PluginMotion.SmoothStep(progress), 5);
    }

    /// <summary>
    /// Verifies cubic ease-out clamps and uses the canonical curve.
    /// </summary>
    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(0.5f, 0.875f)]
    [InlineData(1f, 1f)]
    [InlineData(2f, 1f)]
    public void EaseOutCubicIsBounded(float progress, float expected)
    {
        Assert.Equal(expected, PluginMotion.EaseOutCubic(progress), 5);
    }

    /// <summary>
    /// Verifies the working pulse bounds and reduced-motion static state.
    /// </summary>
    [Fact]
    public void WorkingPulseUsesCanonicalBoundsAndStaticReducedMotion()
    {
        Assert.Equal(0f, PluginMotion.WorkingPulse(0f, false), 5);
        Assert.Equal(1f, PluginMotion.WorkingPulse(PluginMotion.WorkingPulseSeconds / 2f, false), 5);
        Assert.Equal(0.5f, PluginMotion.WorkingPulse(PluginMotion.WorkingPulseSeconds * 0.75f, false), 5);
        Assert.Equal(0.25f, PluginMotion.WorkingPulse(PluginMotion.WorkingPulseSeconds * 0.875f, false), 5);
        Assert.Equal(0f, PluginMotion.WorkingPulse(PluginMotion.WorkingPulseSeconds, false), 5);
        Assert.Equal(1f, PluginMotion.WorkingPulse(-PluginMotion.WorkingPulseSeconds / 2f, false), 5);
        Assert.Equal(0.5f, PluginMotion.WorkingPulse(0f, true), 5);
    }
}
