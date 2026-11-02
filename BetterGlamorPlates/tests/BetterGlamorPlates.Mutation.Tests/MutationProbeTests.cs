namespace BetterGlamorPlates.Tests;

using BetterGlamorPlates.Mutation;

/// <summary>Proves behavior-changing probe mutations reach the test process.</summary>
public sealed class MutationProbeTests
{
    /// <summary>Distinguishes negative, zero, and positive boundary values.</summary>
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void IsPositiveHonorsTheZeroBoundary(int value, bool expected)
    {
        Assert.Equal(expected, MutationProbe.IsPositive(value));
    }
}
