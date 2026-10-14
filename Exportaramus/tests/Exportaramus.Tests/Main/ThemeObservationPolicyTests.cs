using Exportaramus.Shared.Ui;

namespace Exportaramus.Tests.Main;

public sealed class ThemeObservationPolicyTests
{
    [Fact]
    public void SamplesImmediatelyThenOnlyAtTheOneSecondBoundary()
    {
        var reads = 0;
        var applied = new List<uint>();
        var policy = new ThemeObservationPolicy(() => (uint)++reads, applied.Add);
        policy.Update(0);
        policy.Update(.999);
        Assert.Equal(new uint[] { 1 }, applied);
        policy.Update(1);
        policy.Update(1);
        Assert.Equal(new uint[] { 1, 2 }, applied);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrThrowingReadRetainsPriorPaletteAndCanRecover(bool throws)
    {
        var reads = 0;
        var selected = ThemePolicy.Resolve(0);
        var policy = new ThemeObservationPolicy(() =>
        {
            reads++;
            if (reads == 2)
            {
                if (throws)
                {
                    throw new InvalidOperationException();
                }
                return null;
            }
            return reads == 1 ? 4u : uint.MaxValue;
        }, theme => selected = ThemePolicy.Resolve(theme));
        policy.Update(0);
        Assert.Same(ThemePolicy.Resolve(4), selected);
        policy.Update(1);
        Assert.Same(ThemePolicy.Resolve(4), selected);
        policy.Update(1.999);
        Assert.Equal(2, reads);
        policy.Update(2);
        Assert.Same(ThemePolicy.Resolve(0), selected);
    }
}
