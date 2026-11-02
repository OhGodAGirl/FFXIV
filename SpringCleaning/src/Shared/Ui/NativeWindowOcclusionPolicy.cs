using System.Numerics;

namespace SpringCleaning;

internal static class NativeWindowOcclusionPolicy
{
    // Focus is not stacking order: only native windows above the anchor cover its tabs.
    internal static bool IsInFront(uint anchorLayer, ushort anchorOrder, uint candidateLayer, ushort candidateOrder)
        => candidateLayer > anchorLayer || candidateLayer == anchorLayer && candidateOrder > anchorOrder;

    internal readonly record struct Region(Vector2 Minimum, Vector2 Maximum)
    {
        public Vector2 Size => Maximum - Minimum;
    }

    public static float GetOpacity(
        byte nativeAlpha,
        bool hiddenByCurrentVisibility,
        bool hiddenByAppliedVisibility)
    {
        return hiddenByCurrentVisibility || hiddenByAppliedVisibility
            ? 0f
            : nativeAlpha / 255f;
    }

    public static bool ShouldOccludeNativeWindow(
        bool isAnchor,
        bool isVisible,
        bool hasRootNode,
        bool hasWindowSurface,
        float opacity)
    {
        return !isAnchor
            && isVisible
            && hasRootNode
            && hasWindowSurface
            && opacity > 0f;
    }

    public static bool Overlaps(Region first, Region second)
    {
        return HasArea(first)
            && HasArea(second)
            && first.Minimum.X < second.Maximum.X
            && first.Maximum.X > second.Minimum.X
            && first.Minimum.Y < second.Maximum.Y
            && first.Maximum.Y > second.Minimum.Y;
    }

    public static bool Contains(Region region, Vector2 point)
    {
        return HasArea(region)
            && point.X >= region.Minimum.X
            && point.X < region.Maximum.X
            && point.Y >= region.Minimum.Y
            && point.Y < region.Maximum.Y;
    }

    public static IReadOnlyList<Region> GetVisibleRegions(
        Region surface,
        IReadOnlyList<Region> occlusions)
    {
        if (!HasArea(surface))
            return [];

        List<Region> visible = [surface];
        foreach (var occlusion in occlusions)
        {
            List<Region> next = [];
            foreach (var region in visible)
                Subtract(region, occlusion, next);
            visible = next;
        }

        return visible;
    }

    private static bool HasArea(Region region)
                => region.Maximum.X > region.Minimum.X
        && region.Maximum.Y > region.Minimum.Y;

    private static void Subtract(Region surface, Region occlusion, List<Region> output)
    {
        if (!Overlaps(surface, occlusion))
        {
            output.Add(surface);
            return;
        }

        var intersectionMinimum = Vector2.Max(surface.Minimum, occlusion.Minimum);
        var intersectionMaximum = Vector2.Min(surface.Maximum, occlusion.Maximum);
        Add(surface.Minimum, new Vector2(surface.Maximum.X, intersectionMinimum.Y));
        Add(new Vector2(surface.Minimum.X, intersectionMaximum.Y), surface.Maximum);
        Add(
            new Vector2(surface.Minimum.X, intersectionMinimum.Y),
            new Vector2(intersectionMinimum.X, intersectionMaximum.Y));
        Add(
            new Vector2(intersectionMaximum.X, intersectionMinimum.Y),
            new Vector2(surface.Maximum.X, intersectionMaximum.Y));

        void Add(Vector2 minimum, Vector2 maximum)
        {
            var region = new Region(minimum, maximum);
            if (HasArea(region))
                output.Add(region);
        }
    }
}
