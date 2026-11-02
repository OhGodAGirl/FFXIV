namespace CollectionShopper.Tests;

/// <summary>
/// Verifies the shared motion tokens remain bounded and accessible.
/// </summary>
public sealed class PluginMotionTests
{
    [Fact]
    public void WorkingClockRotatesAndHonorsReducedMotion()
    {
        Assert.Equal(-MathF.PI / 2f, PluginMotion.WorkingClockAngle(0d, false));
        Assert.Equal(0f, PluginMotion.WorkingClockAngle(0.3d, false), 5);
        Assert.Equal(MathF.PI / 2f, PluginMotion.WorkingClockAngle(0.6d, false), 5);
        Assert.Equal(PluginMotion.WorkingClockAngle(0d, false), PluginMotion.WorkingClockAngle(1.2d, false));
        Assert.Equal(-MathF.PI / 2f, PluginMotion.WorkingClockAngle(0.6d, true));
    }

    [Theory]
    [InlineData(0.0, 0.0f, -1.0f)]
    [InlineData(0.3, 1.0f, 0.0f)]
    [InlineData(0.6, 0.0f, 1.0f)]
    [InlineData(0.9, -1.0f, 0.0f)]
    public void ClockHandKeepsItsClockwiseQuarterTurnsAcrossRepeatedCycles(double elapsed, float x, float y)
    {
        foreach (var cycle in new[] { 0, 1, 100, 10_000 })
        {
            var angle = PluginMotion.WorkingClockAngle(elapsed + cycle * 1.2d, false);
            Assert.Equal(x, MathF.Cos(angle), 4);
            Assert.Equal(y, MathF.Sin(angle), 4);
            Assert.Equal(-MathF.PI / 2f, PluginMotion.WorkingClockAngle(elapsed + cycle * 1.2d, true));
        }
    }

    /// <summary>
    /// Verifies progress clamps invalid boundaries and snaps under reduced motion.
    /// </summary>
    [Theory]
    [InlineData(-1f, 1f, false, 0f)]
    [InlineData(0.5f, 1f, false, 0.5f)]
    [InlineData(0.25f, 2f, false, 0.125f)]
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
        // Arrange and Act
        var progress = PluginMotion.Progress(elapsed, duration, reduceMotion);

        // Assert
        Assert.Equal(expected, progress, 5);
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
        // Arrange and Act
        var result = PluginMotion.SmoothStep(progress);

        // Assert
        Assert.Equal(expected, result, 5);
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
        // Arrange and Act
        var result = PluginMotion.EaseOutCubic(progress);

        // Assert
        Assert.Equal(expected, result, 5);
    }

    /// <summary>
    /// Verifies the working pulse bounds and reduced-motion static state.
    /// </summary>
    [Fact]
    public void WorkingPulseUsesCanonicalBoundsAndStaticReducedMotion()
    {
        // Arrange and Act
        var start = PluginMotion.WorkingPulse(0f, false);
        var peak = PluginMotion.WorkingPulse(PluginMotion.WorkingPulseSeconds / 2f, false);
        var end = PluginMotion.WorkingPulse(PluginMotion.WorkingPulseSeconds, false);
        var negativePeak = PluginMotion.WorkingPulse(-PluginMotion.WorkingPulseSeconds / 2f, false);
        var reduced = PluginMotion.WorkingPulse(0f, true);

        // Assert
        Assert.Equal(0f, start, 5);
        Assert.Equal(1f, peak, 5);
        Assert.Equal(0f, end, 5);
        Assert.Equal(1f, negativePeak, 5);
        Assert.Equal(0.5f, reduced, 5);
    }

    /// <summary>
    /// Verifies both triangular pulse slopes and negative-cycle normalization.
    /// </summary>
    [Theory]
    [InlineData(0.225f, 0.5f)]
    [InlineData(0.675f, 0.5f)]
    [InlineData(0.3375f, 0.75f)]
    [InlineData(0.5625f, 0.75f)]
    [InlineData(-0.225f, 0.5f)]
    [InlineData(-1.125f, 0.5f)]
    [InlineData(1f, 0.22222222f)]
    public void WorkingPulseUsesExactTriangularCurve(float elapsed, float expected)
    {
        var pulse = PluginMotion.WorkingPulse(elapsed, false);

        Assert.Equal(expected, pulse, 5);
    }

    /// <summary>
    /// Verifies generated motion samples stay bounded, periodic, and monotonic in progress.
    /// </summary>
    [Fact]
    public void GeneratedMotionSamplesRemainBoundedPeriodicAndMonotonic()
    {
        var random = new Random(20260901);
        var previousProgress = 0f;
        for (var iteration = 0; iteration < 512; iteration++)
        {
            var elapsed = (float)((random.NextDouble() * 200d) - 100d);
            var pulse = PluginMotion.WorkingPulse(elapsed, false);
            var periodic = PluginMotion.WorkingPulse(elapsed + PluginMotion.WorkingPulseSeconds, false);
            var progress = PluginMotion.Progress(iteration, 511f, false);

            Assert.InRange(pulse, 0f, 1f);
            Assert.InRange(MathF.Abs(pulse - periodic), 0f, 0.00002f);
            Assert.True(progress >= previousProgress);
            Assert.InRange(PluginMotion.SmoothStep(progress), 0f, 1f);
            Assert.InRange(PluginMotion.EaseOutCubic(progress), 0f, 1f);
            previousProgress = progress;
        }
    }
}
