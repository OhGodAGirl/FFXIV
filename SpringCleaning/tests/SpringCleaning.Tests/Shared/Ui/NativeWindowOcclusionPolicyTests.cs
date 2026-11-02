using System.Numerics;

namespace SpringCleaning.Tests;

public sealed class NativeWindowOcclusionPolicyTests
{
    [Theory]
    [InlineData(2, 4, 1, 9, false)]
    [InlineData(2, 4, 2, 3, false)]
    [InlineData(2, 4, 2, 4, false)]
    [InlineData(2, 4, 2, 5, true)]
    [InlineData(2, 4, 3, 0, true)]
    public void OnlyWindowsAboveTheAnchorOccludeTabs(uint layer, ushort order, uint otherLayer, ushort otherOrder, bool expected)
        => Assert.Equal(expected, NativeWindowOcclusionPolicy.IsInFront(layer, order, otherLayer, otherOrder));

    [Theory]
    [InlineData(255, false, false, 1f)]
    [InlineData(128, false, false, 128f / 255f)]
    [InlineData(255, true, false, 0f)]
    [InlineData(255, false, true, 0f)]
    [InlineData(255, true, true, 0f)]
    public void OpacityTracksNativeAlphaAndBothModalVisibilityStates(
        byte alpha,
        bool currentHidden,
        bool appliedHidden,
        float expected)
    {
        Assert.Equal(
            expected,
            NativeWindowOcclusionPolicy.GetOpacity(alpha, currentHidden, appliedHidden));
    }

    [Theory]
    [InlineData(false, true, true, true, 1f, true)]
    [InlineData(false, true, true, true, 0.01f, true)]
    [InlineData(true, true, true, true, 1f, false)]
    [InlineData(false, false, true, true, 1f, false)]
    [InlineData(false, true, false, true, 1f, false)]
    [InlineData(false, true, true, false, 1f, false)]
    [InlineData(false, true, true, true, 0f, false)]
    [InlineData(false, true, true, true, -0.01f, false)]
    public void EveryVisibleOverlappingNativeWindowSurfaceOccludesTheRail(
        bool isAnchor,
        bool isVisible,
        bool hasRootNode,
        bool hasWindowSurface,
        float opacity,
        bool expected)
    {
        Assert.Equal(
            expected,
            NativeWindowOcclusionPolicy.ShouldOccludeNativeWindow(
                isAnchor,
                isVisible,
                hasRootNode,
                hasWindowSurface,
                opacity));
    }

    [Fact]
    public void RectangleOverlapIsStrictSymmetricAndRejectsEmptyRegions()
    {
        var first = Region(0, 0, 10, 10);
        var overlapping = Region(9, 2, 12, 8);
        var touching = Region(10, 0, 20, 10);
        var emptyWidth = Region(5, 0, 5, 10);
        var emptyHeight = Region(0, 5, 10, 5);

        Assert.True(NativeWindowOcclusionPolicy.Overlaps(first, overlapping));
        Assert.True(NativeWindowOcclusionPolicy.Overlaps(overlapping, first));
        Assert.False(NativeWindowOcclusionPolicy.Overlaps(first, touching));
        Assert.False(NativeWindowOcclusionPolicy.Overlaps(first, emptyWidth));
        Assert.False(NativeWindowOcclusionPolicy.Overlaps(emptyHeight, first));
    }

    [Fact]
    public void PointContainmentUsesInclusiveMinimumAndExclusiveMaximum()
    {
        var region = Region(2, 3, 8, 9);

        Assert.True(NativeWindowOcclusionPolicy.Contains(region, new Vector2(2, 3)));
        Assert.True(NativeWindowOcclusionPolicy.Contains(region, new Vector2(7.999f, 8.999f)));
        Assert.False(NativeWindowOcclusionPolicy.Contains(region, new Vector2(8, 5)));
        Assert.False(NativeWindowOcclusionPolicy.Contains(region, new Vector2(5, 9)));
        Assert.False(NativeWindowOcclusionPolicy.Contains(region, new Vector2(1.999f, 5)));
        Assert.False(NativeWindowOcclusionPolicy.Contains(region, new Vector2(5, 2.999f)));
        Assert.False(NativeWindowOcclusionPolicy.Contains(Region(4, 4, 4, 8), new Vector2(4, 5)));
    }

    [Fact]
    public void CentralOccluderSplitsSurfaceIntoFourNonOverlappingRegions()
    {
        var visible = NativeWindowOcclusionPolicy.GetVisibleRegions(
            Region(0, 0, 10, 10),
            [Region(2, 3, 8, 7)]);

        Assert.Equal(
            [
                Region(0, 0, 10, 3),
                Region(0, 7, 10, 10),
                Region(0, 3, 2, 7),
                Region(8, 3, 10, 7),
            ],
            visible);
    }

    [Fact]
    public void EmptyIrrelevantAndMultipleOccludersAreHandledExactly()
    {
        var surface = Region(0, 0, 10, 10);

        Assert.Empty(NativeWindowOcclusionPolicy.GetVisibleRegions(
            Region(0, 0, 0, 10),
            []));
        Assert.Equal(
            [surface],
            NativeWindowOcclusionPolicy.GetVisibleRegions(
                surface,
                [Region(2, 2, 2, 9), Region(20, 20, 30, 30)]));
        Assert.Empty(NativeWindowOcclusionPolicy.GetVisibleRegions(
            surface,
            [Region(0, 0, 5, 10), Region(5, 0, 10, 10)]));
    }

    [Fact]
    public void SeededOcclusionGeometryNeverDrawsInsideAHigherNativeWindow()
    {
        var random = new Random(0x51A7);
        for (var iteration = 0; iteration < 1_000; iteration++)
        {
            var surface = RandomRegion(random, 5, 80);
            var occlusions = Enumerable.Range(0, 3)
                .Select(_ => RandomRegion(random, 1, 35))
                .ToArray();
            var visible = NativeWindowOcclusionPolicy.GetVisibleRegions(surface, occlusions);

            Assert.All(visible, region =>
            {
                Assert.True(region.Size.X > 0f);
                Assert.True(region.Size.Y > 0f);
                Assert.True(region.Minimum.X >= surface.Minimum.X);
                Assert.True(region.Minimum.Y >= surface.Minimum.Y);
                Assert.True(region.Maximum.X <= surface.Maximum.X);
                Assert.True(region.Maximum.Y <= surface.Maximum.Y);
                Assert.DoesNotContain(
                    occlusions,
                    occlusion => NativeWindowOcclusionPolicy.Overlaps(region, occlusion));
            });
        }
    }

    private static NativeWindowOcclusionPolicy.Region RandomRegion(
        Random random,
        int minimumSize,
        int maximumSize)
    {
        var minimum = new Vector2(random.Next(-40, 80), random.Next(-40, 80));
        var size = new Vector2(
            random.Next(minimumSize, maximumSize),
            random.Next(minimumSize, maximumSize));
        return new NativeWindowOcclusionPolicy.Region(minimum, minimum + size);
    }

    private static NativeWindowOcclusionPolicy.Region Region(
        float left,
        float top,
        float right,
        float bottom)
        => new(new Vector2(left, top), new Vector2(right, bottom));
}
