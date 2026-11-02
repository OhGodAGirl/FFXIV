using System.Numerics;

namespace CollectionShopper;

internal static class MarketBoardTabPresentation
{
    internal static Vector2 RailPosition(Vector2 marketPosition, Vector2 visibleFramePosition,
        float visibleFrameHeight, float viewportLeft, int visibleTabCount, float scale)
    {
        var railWidth = (TabBodySize + TabExtension) * scale;
        var left = visibleFramePosition.X - railWidth + TabExtension * scale;
        if (left < viewportLeft + 2f)
            left = marketPosition.X + 4f;
        var railHeight = (visibleTabCount * TabBodySize + (visibleTabCount - 1) * TabGap) * scale;
        return new Vector2(left, visibleFramePosition.Y + (visibleFrameHeight - railHeight) * 0.5f);
    }

    internal readonly record struct TabPalette(Vector4 Surface, Vector4 Hover, Vector4 Active, Vector4 Border, Vector4 Icon, Vector4 Inset);

    internal static TabPalette ResolveTheme(uint theme)
    {
        var light = theme is 1 or 4 or 7;
        var surface = theme switch
        {
            1 => new Vector4(.94f, .81f, .65f, 1f),
            2 => new Vector4(.22f, .10f, .76f, 1f),
            3 => new Vector4(.22f, .32f, .55f, 1f),
            4 => new Vector4(.78f, .78f, .80f, 1f),
            5 => new Vector4(.27f, .46f, .16f, 1f),
            6 => new Vector4(.30f, .31f, .32f, 1f),
            7 => new Vector4(.93f, .63f, .85f, 1f),
            _ => new Vector4(.27f, .275f, .27f, 1f),
        };
        var hoverOffset = new Vector4(.045f, .045f, .045f, 0f);
        var activeOffset = new Vector4(.08f, .08f, .08f, 0f);
        return new(surface,
            light ? surface - hoverOffset : surface + hoverOffset,
            light ? surface - activeOffset : surface + activeOffset,
            light ? new(.48f, .40f, .27f, 1f) : new(.72f, .62f, .42f, 1f),
            light ? new(.41f, .28f, .045f, 1f) : new(.91f, .76f, .43f, 1f),
            surface - new Vector4(.04f, .04f, .04f, 0f));
    }

    internal enum TabStyle
    {
        InsetSockets = 0,
        FlushIndex = 1,
        RibbonTabs = 2,
        RoundTabs = 3,
        BeveledIndex = 4,
    }

    internal const float TabBodySize = 48f;
    internal const float TabExtension = 2f;
    internal const float TabIconSize = 32f;
    internal const float TabGap = 5f;

    internal static TabStyle NormalizeTabStyle(TabStyle style)
        => style is >= TabStyle.InsetSockets and <= TabStyle.BeveledIndex
            ? style
            : TabStyle.RoundTabs;

    internal static string GetTabStyleName(TabStyle style) => NormalizeTabStyle(style) switch
    {
        TabStyle.InsetSockets => "Inset Sockets",
        TabStyle.FlushIndex => "Flush Index",
        TabStyle.RibbonTabs => "Ribbon Tabs",
        TabStyle.BeveledIndex => "Beveled Index",
        _ => "Round Tabs",
    };

    internal static bool CanFocusFromClick(bool surfaceHovered, bool leftPressed, bool inputCovered, bool nativeOrderAvailable)
        => surfaceHovered && leftPressed && !inputCovered && nativeOrderAvailable;

    internal static bool IsOccludingPanel(bool isAnchor, bool visible, bool hasWindowFrame, bool knownPopup)
        => isAnchor || (visible && (hasWindowFrame || knownPopup));

    internal readonly record struct ClipRegion(Vector2 Minimum, Vector2 Maximum);
    internal readonly record struct WindowPlacement(Vector2 Position, Vector2 Size);
    internal static WindowPlacement GetInventoryVisibleFrame(Vector2 position, Vector2 size)
        // The expanded Inventory component includes transparent frame padding.
        // Insets measured from the live reference scale with its native bounds.
        => new(position + new Vector2(size.X * 4f / 615f, size.Y * 4f / 566f),
            new Vector2(size.X * 607f / 615f, size.Y * 548f / 566f));

    internal readonly record struct NativeWindowSnapshot(
        nint Address,
        uint DepthLayer,
        int LayerIndex,
        ClipRegion Bounds);

    internal static bool IsInFrontOf(
        uint anchorDepthLayer,
        int anchorLayerIndex,
        uint candidateDepthLayer,
        int candidateLayerIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(anchorLayerIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(candidateLayerIndex);
        // Native collision traversal starts at the highest layer and takes its
        // first matching entry: within a layer, lower indices are in front.
        return candidateDepthLayer > anchorDepthLayer
            || (candidateDepthLayer == anchorDepthLayer && candidateLayerIndex < anchorLayerIndex);
    }

    internal static bool TryFillNativeOcclusions(
        nint anchorAddress,
        IReadOnlyList<NativeWindowSnapshot> windows,
        int maximumOccluders,
        List<ClipRegion> occlusions)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(occlusions);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumOccluders);
        occlusions.Clear();
        NativeWindowSnapshot? anchor = null;
        foreach (var window in windows)
        {
            if (window.Address == 0 || window.LayerIndex < 0)
                return false;

            if (window.Address == anchorAddress)
                anchor = window;
        }

        if (anchor is not { } marketBoard)
            return false;

        foreach (var window in windows)
        {
            if (!IsInFrontOf(
                    marketBoard.DepthLayer,
                    marketBoard.LayerIndex,
                    window.DepthLayer,
                    window.LayerIndex))
                continue;

            if (occlusions.Count == maximumOccluders)
            {
                occlusions.Clear();
                return false;
            }

            occlusions.Add(window.Bounds);
        }

        return true;
    }

    internal static WindowPlacement GetLockedCompanionPlacement(
        Vector2 marketBoardPosition,
        Vector2 marketBoardSize,
        float gap)
        => new(
            new Vector2(
                marketBoardPosition.X + marketBoardSize.X + gap,
                marketBoardPosition.Y + (marketBoardSize.Y * 4f / 756f)),
            // Item Search's visible frame is inset within its reported bounds.
            // Ratios measured from the user's reference preserve the inset at
            // different native UI scales without changing horizontal placement.
            new Vector2(marketBoardSize.X, marketBoardSize.Y * 736f / 756f));

    internal static float GetOpacity(
        byte nativeAlpha,
        bool hiddenByCurrentVisibility,
        bool hiddenByAppliedVisibility)
    {
        if (hiddenByCurrentVisibility || hiddenByAppliedVisibility)
            return 0f;

        return nativeAlpha / 255f;
    }

    internal static bool Overlaps(
        Vector2 firstPosition,
        Vector2 firstSize,
        Vector2 secondPosition,
        Vector2 secondSize)
    {
        if (firstSize.X <= 0f || firstSize.Y <= 0f || secondSize.X <= 0f || secondSize.Y <= 0f)
            return false;

        return firstPosition.X < secondPosition.X + secondSize.X
            && firstPosition.X + firstSize.X > secondPosition.X
            && firstPosition.Y < secondPosition.Y + secondSize.Y
            && firstPosition.Y + firstSize.Y > secondPosition.Y;
    }

    internal static bool Contains(Vector2 position, Vector2 size, Vector2 point)
        => point.X >= position.X
            && point.X < position.X + size.X
            && point.Y >= position.Y
            && point.Y < position.Y + size.Y;

    internal static IReadOnlyList<ClipRegion> GetVisibleRegions(
        Vector2 position,
        Vector2 size,
        Vector2 occlusionPosition,
        Vector2 occlusionSize)
        => GetVisibleRegions(
            position,
            size,
            [new ClipRegion(occlusionPosition, occlusionPosition + occlusionSize)]);

    internal static IReadOnlyList<ClipRegion> GetVisibleRegions(
        Vector2 position,
        Vector2 size,
        IReadOnlyList<ClipRegion> occlusions)
    {
        var regions = new List<ClipRegion>(4);
        var scratch = new List<ClipRegion>(4);
        FillVisibleRegions(position, size, occlusions, regions, scratch);
        return regions;
    }

    internal static void FillVisibleRegions(
        Vector2 position,
        Vector2 size,
        IReadOnlyList<ClipRegion> occlusions,
        List<ClipRegion> regions,
        List<ClipRegion> scratch)
    {
        ArgumentNullException.ThrowIfNull(occlusions);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(scratch);
        if (ReferenceEquals(regions, scratch))
            throw new ArgumentException("Visible-region buffers must be distinct.", nameof(scratch));

        var maximum = position + size;
        regions.Clear();
        scratch.Clear();
        if (size.X <= 0f || size.Y <= 0f)
            return;

        regions.Add(new ClipRegion(position, maximum));
        var current = regions;
        var next = scratch;
        foreach (var occlusion in occlusions)
        {
            next.Clear();
            foreach (var region in current)
                AddVisibleRegions(region, occlusion, next);

            (current, next) = (next, current);
        }

        if (ReferenceEquals(current, regions))
            return;

        regions.Clear();
        regions.AddRange(current);
    }

    internal static bool IsPointOccluded(
        Vector2 point,
        IReadOnlyList<ClipRegion> occlusions)
    {
        ArgumentNullException.ThrowIfNull(occlusions);
        foreach (var occlusion in occlusions)
        {
            if (Contains(occlusion.Minimum, occlusion.Maximum - occlusion.Minimum, point))
                return true;
        }

        return false;
    }

    private static void AddVisibleRegions(
        ClipRegion region,
        ClipRegion occlusion,
        List<ClipRegion> destination)
    {
        var regionSize = region.Maximum - region.Minimum;
        var occlusionSize = occlusion.Maximum - occlusion.Minimum;
        if (!Overlaps(region.Minimum, regionSize, occlusion.Minimum, occlusionSize))
        {
            destination.Add(region);
            return;
        }

        var intersectionMinimum = Vector2.Max(region.Minimum, occlusion.Minimum);
        var intersectionMaximum = Vector2.Min(region.Maximum, occlusion.Maximum);

        AddRegion(region.Minimum, new Vector2(region.Maximum.X, intersectionMinimum.Y));
        AddRegion(new Vector2(region.Minimum.X, intersectionMaximum.Y), region.Maximum);
        AddRegion(
            new Vector2(region.Minimum.X, intersectionMinimum.Y),
            new Vector2(intersectionMinimum.X, intersectionMaximum.Y));
        AddRegion(
            new Vector2(intersectionMaximum.X, intersectionMinimum.Y),
            new Vector2(region.Maximum.X, intersectionMaximum.Y));

        void AddRegion(Vector2 minimum, Vector2 regionMaximum)
        {
            if (regionMaximum.X > minimum.X && regionMaximum.Y > minimum.Y)
                destination.Add(new ClipRegion(minimum, regionMaximum));
        }
    }

}








