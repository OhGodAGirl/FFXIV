using System.Numerics;

namespace CollectionShopper.Tests;

/// <summary>
/// Verifies the side tabs follow the native Market Board presentation state.
/// </summary>
public sealed class MarketBoardTabPresentationTests
{
    [Theory]
    [InlineData(0u, .27f, .275f, .27f, false)]
    [InlineData(1u, .94f, .81f, .65f, true)]
    [InlineData(2u, .22f, .10f, .76f, false)]
    [InlineData(3u, .22f, .32f, .55f, false)]
    [InlineData(4u, .78f, .78f, .80f, true)]
    [InlineData(5u, .27f, .46f, .16f, false)]
    [InlineData(6u, .30f, .31f, .32f, false)]
    [InlineData(7u, .93f, .63f, .85f, true)]
    public void NativeThemePreservesApprovedPaletteAndInteractionContrast(uint theme, float r, float g, float b, bool light)
    {
        var palette = MarketBoardTabPresentation.ResolveTheme(theme);
        Assert.Equal(new Vector4(r, g, b, 1f), palette.Surface);
        var direction = light ? -1f : 1f;
        Assert.Equal(palette.Surface + new Vector4(.045f, .045f, .045f, 0) * direction, palette.Hover);
        Assert.Equal(palette.Surface + new Vector4(.08f, .08f, .08f, 0) * direction, palette.Active);
        Assert.Equal(palette.Surface - new Vector4(.04f, .04f, .04f, 0), palette.Inset);
        Assert.Equal(light ? new Vector4(.48f, .40f, .27f, 1) : new Vector4(.72f, .62f, .42f, 1), palette.Border);
        Assert.Equal(light ? new Vector4(.41f, .28f, .045f, 1) : new Vector4(.91f, .76f, .43f, 1), palette.Icon);
        var surfaceLuminance = Luminance(palette.Surface);
        var iconLuminance = Luminance(palette.Icon);
        Assert.True((Math.Max(surfaceLuminance, iconLuminance) + .05) / (Math.Min(surfaceLuminance, iconLuminance) + .05) >= 3);
        Assert.Equal(MarketBoardTabPresentation.ResolveTheme(0), MarketBoardTabPresentation.ResolveTheme(uint.MaxValue));

        static double Luminance(Vector4 color)
        {
            static double Linear(float value) => value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
            return .2126 * Linear(color.X) + .7152 * Linear(color.Y) + .0722 * Linear(color.Z);
        }
    }
    [Theory]
    [InlineData(0, "Inset Sockets")]
    [InlineData(1, "Flush Index")]
    [InlineData(2, "Ribbon Tabs")]
    [InlineData(3, "Round Tabs")]
    [InlineData(4, "Beveled Index")]
    public void SavedTabStylesKeepTheirNumericIdentityAndPlayerLabels(int value, string label)
    {
        var style = (MarketBoardTabPresentation.TabStyle)value;
        Assert.Equal(style, MarketBoardTabPresentation.NormalizeTabStyle(style));
        Assert.Equal(label, MarketBoardTabPresentation.GetTabStyleName(style));
        var saved = System.Text.Json.JsonSerializer.Serialize(style);
        Assert.Equal(value.ToString(System.Globalization.CultureInfo.InvariantCulture), saved);
        Assert.Equal(style, System.Text.Json.JsonSerializer.Deserialize<MarketBoardTabPresentation.TabStyle>(saved));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void UnsupportedSavedTabStyleFallsBackToRoundTabs(int value)
    {
        var style = (MarketBoardTabPresentation.TabStyle)value;
        Assert.Equal(MarketBoardTabPresentation.TabStyle.RoundTabs,
            MarketBoardTabPresentation.NormalizeTabStyle(style));
        Assert.Equal("Round Tabs", MarketBoardTabPresentation.GetTabStyleName(style));
    }

    [Theory]
    [InlineData(0.75f)]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void ExpandedInventoryVisualPaddingScalesWithoutChangingInputBounds(float scale)
    {
        var position = new Vector2(100, 200);
        var size = new Vector2(615, 566) * scale;
        var frame = MarketBoardTabPresentation.GetInventoryVisibleFrame(position, size);
        Assert.Equal(position + new Vector2(4, 4) * scale, frame.Position);
        Assert.Equal(new Vector2(607, 548) * scale, frame.Size);
        var padding = position + new Vector2(2, 2) * scale;
        Assert.True(MarketBoardTabPresentation.IsPointOccluded(padding, [new(position, position + size)]));
        Assert.False(MarketBoardTabPresentation.IsPointOccluded(padding, [new(frame.Position, frame.Position + frame.Size)]));
    }

    [Fact]
    public void FocusRequiresAnUncoveredDeliberateClickWithCurrentNativeOrder()
    {
        for (var mask = 0; mask < 16; mask++)
        {
            var hovered = (mask & 1) != 0;
            var pressed = (mask & 2) != 0;
            var covered = (mask & 4) != 0;
            var ordered = (mask & 8) != 0;
            Assert.Equal(mask == 11, MarketBoardTabPresentation.CanFocusFromClick(hovered, pressed, covered, ordered));
        }
    }

    [Fact]
    public void TransparentHudRegionsAreNotWindowOccludersButNativeTooltipsAre()
    {
        Assert.False(MarketBoardTabPresentation.IsOccludingPanel(false, true, false, false));
        Assert.True(MarketBoardTabPresentation.IsOccludingPanel(false, true, false, true));
        Assert.True(MarketBoardTabPresentation.IsOccludingPanel(false, true, true, false));
        Assert.False(MarketBoardTabPresentation.IsOccludingPanel(false, false, true, true));
        Assert.True(MarketBoardTabPresentation.IsOccludingPanel(true, false, false, false));
    }

    [Fact]
    public void InvisibleInventoryHitPaddingBlocksInputWithoutCuttingOutTheCompanion()
    {
        MarketBoardTabPresentation.ClipRegion[] visual = [new(new(100, 100), new(200, 200))];
        MarketBoardTabPresentation.ClipRegion[] input = [new(new(96, 96), new(204, 212))];
        var paddingPoint = new Vector2(150, 205);
        Assert.True(MarketBoardTabPresentation.IsPointOccluded(paddingPoint, input));
        Assert.False(MarketBoardTabPresentation.IsPointOccluded(paddingPoint, visual));
        var visible = MarketBoardTabPresentation.GetVisibleRegions(new(0, 0), new(400, 400), visual);
        Assert.True(MarketBoardTabPresentation.IsPointOccluded(paddingPoint, visible));
        Assert.False(MarketBoardTabPresentation.CanFocusFromClick(true, true,
            MarketBoardTabPresentation.IsPointOccluded(paddingPoint, input), true));
    }

    [Theory]
    [InlineData(4u, 0, true)]
    [InlineData(3u, 101, false)]
    [InlineData(3u, 100, false)]
    [InlineData(3u, 99, true)]
    [InlineData(2u, 255, false)]
    public void HigherNativeZOrderIsInFrontOfMarketBoard(
        uint candidateDepthLayer,
        int candidateLayerIndex,
        bool expected)
    {
        Assert.Equal(expected, MarketBoardTabPresentation.IsInFrontOf(
            anchorDepthLayer: 3,
            anchorLayerIndex: 100,
            candidateDepthLayer,
            candidateLayerIndex));
    }

    [Fact]
    public void SameLayerListOrderRejectsNegativeIndexes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MarketBoardTabPresentation.IsInFrontOf(3, -1, 3, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MarketBoardTabPresentation.IsInFrontOf(3, 0, 3, -1));
    }

    [Fact]
    public void ForegroundInventoryClipsTabsAndCompanionAndOwnsOnlyCoveredInput()
    {
        var inventory = new MarketBoardTabPresentation.ClipRegion(
            new Vector2(50f, 100f), new Vector2(1200f, 500f));
        var windows = new MarketBoardTabPresentation.NativeWindowSnapshot[]
        {
            new(100, 3, 3, new(new Vector2(200f, 0f), new Vector2(900f, 700f))),
            new(200, 3, 2, inventory),
        };
        var occlusions = new List<MarketBoardTabPresentation.ClipRegion>();

        // Enumeration order and the last focus event do not determine the stack.
        Assert.True(MarketBoardTabPresentation.TryFillNativeOcclusions(100, windows, 32, occlusions));
        Assert.Equal(inventory, Assert.Single(occlusions));
        Assert.Empty(MarketBoardTabPresentation.GetVisibleRegions(
            new Vector2(100f, 200f), new Vector2(98f, 74f), occlusions));
        Assert.Equal(new MarketBoardTabPresentation.ClipRegion(
                new Vector2(100f, 50f), new Vector2(198f, 100f)),
            Assert.Single(MarketBoardTabPresentation.GetVisibleRegions(
                new Vector2(100f, 50f), new Vector2(98f, 74f), occlusions)));
        var companionRegions = MarketBoardTabPresentation.GetVisibleRegions(
            new Vector2(908f, 0f), new Vector2(700f, 700f), occlusions);
        Assert.All(companionRegions, region => Assert.False(MarketBoardTabPresentation.Overlaps(
            region.Minimum, region.Maximum - region.Minimum,
            inventory.Minimum, inventory.Maximum - inventory.Minimum)));
        Assert.True(MarketBoardTabPresentation.IsPointOccluded(new(150f, 200f), occlusions));
        Assert.True(MarketBoardTabPresentation.IsPointOccluded(new(1000f, 200f), occlusions));
        Assert.False(MarketBoardTabPresentation.IsPointOccluded(new(150f, 75f), occlusions));
        Assert.False(MarketBoardTabPresentation.IsPointOccluded(new(1400f, 200f), occlusions));

        // Bringing Market Board forward restores both surfaces on this frame.
        windows[0] = windows[0] with { LayerIndex = 1 };
        Assert.True(MarketBoardTabPresentation.TryFillNativeOcclusions(100, windows, 32, occlusions));
        Assert.Empty(occlusions);
    }

    [Fact]
    public void NativeDepthIncludesHigherDialogsAndLeavesLowerWindowsBehind()
    {
        var bounds = new MarketBoardTabPresentation.ClipRegion(Vector2.Zero, new(100f, 100f));
        var windows = new MarketBoardTabPresentation.NativeWindowSnapshot[]
        {
            new(200, 4, 0, bounds),
            new(300, 2, 255, bounds),
            new(100, 3, 10, bounds),
        };
        var occlusions = new List<MarketBoardTabPresentation.ClipRegion>();

        Assert.True(MarketBoardTabPresentation.TryFillNativeOcclusions(100, windows, 32, occlusions));
        Assert.Equal(bounds, Assert.Single(occlusions));
    }

    [Fact]
    public void ItemDetailTooltipAboveSearchResultsClipsRailWithoutOwningNativeFocus()
    {
        var itemSearch = new MarketBoardTabPresentation.NativeWindowSnapshot(
            100, 3, 20, new(new Vector2(200f, 0f), new Vector2(900f, 700f)));
        var searchResults = new MarketBoardTabPresentation.NativeWindowSnapshot(
            200, 3, 19, new(new Vector2(250f, 0f), new Vector2(900f, 700f)));
        // ItemDetail is a non-window addon. A higher depth layer wins even with
        // a smaller order index and while Search Results retains input focus.
        var itemDetail = new MarketBoardTabPresentation.NativeWindowSnapshot(
            300, 8, 0, new(new Vector2(50f, 120f), new Vector2(500f, 500f)));
        var occlusions = new List<MarketBoardTabPresentation.ClipRegion>();

        Assert.True(MarketBoardTabPresentation.TryFillNativeOcclusions(
            itemSearch.Address, [itemDetail, searchResults, itemSearch], 32, occlusions));
        Assert.Contains(itemDetail.Bounds, occlusions);
        Assert.Empty(MarketBoardTabPresentation.GetVisibleRegions(
            new Vector2(100f, 200f), new Vector2(98f, 74f), occlusions));
        Assert.Equal(new MarketBoardTabPresentation.ClipRegion(
                new Vector2(100f, 100f), new Vector2(198f, 120f)),
            Assert.Single(MarketBoardTabPresentation.GetVisibleRegions(
                new Vector2(100f, 100f), new Vector2(98f, 74f), occlusions)));
        Assert.True(MarketBoardTabPresentation.IsPointOccluded(new(150f, 150f), occlusions));
        Assert.False(MarketBoardTabPresentation.IsPointOccluded(new(150f, 110f), occlusions));

        // The same tooltip also covers the rail when Results is the sole anchor.
        Assert.True(MarketBoardTabPresentation.TryFillNativeOcclusions(
            searchResults.Address, [itemDetail, searchResults], 32, occlusions));
        Assert.Equal(itemDetail.Bounds, Assert.Single(occlusions));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(200, -1)]
    public void UnavailableNativeOrderRejectsTheWholeSnapshotAndClearsPreviousRegions(
        long address, int layerIndex)
    {
        var bounds = new MarketBoardTabPresentation.ClipRegion(Vector2.Zero, Vector2.One);
        var windows = new MarketBoardTabPresentation.NativeWindowSnapshot[]
        {
            new(100, 3, 0, bounds),
            new((nint)address, 3, layerIndex, bounds),
        };
        var occlusions = new List<MarketBoardTabPresentation.ClipRegion> { bounds };

        Assert.False(MarketBoardTabPresentation.TryFillNativeOcclusions(100, windows, 32, occlusions));
        Assert.Empty(occlusions);
    }

    [Fact]
    public void MissingMarketBoardRejectsNativeOrderInsteadOfShowingUnclippedSurfaces()
    {
        var occlusions = new List<MarketBoardTabPresentation.ClipRegion>();
        Assert.False(MarketBoardTabPresentation.TryFillNativeOcclusions(100, [], 32, occlusions));
        Assert.Empty(occlusions);
    }

    [Theory]
    [InlineData(32, true)]
    [InlineData(33, false)]
    public void OccluderLimitRejectsOverflowInsteadOfSilentlyIgnoringForegroundWindows(
        int count, bool expected)
    {
        var bounds = new MarketBoardTabPresentation.ClipRegion(Vector2.Zero, Vector2.One);
        var windows = Enumerable.Range(0, count + 1)
            .Select(index => new MarketBoardTabPresentation.NativeWindowSnapshot(
                index + 100, 3, count - index, bounds)).ToArray();
        var occlusions = new List<MarketBoardTabPresentation.ClipRegion>();

        Assert.Equal(expected,
            MarketBoardTabPresentation.TryFillNativeOcclusions(100, windows, 32, occlusions));
        Assert.Equal(expected ? count : 0, occlusions.Count);
    }

    [Fact]
    public void NativeSnapshotInputsAreRequired()
    {
        var occlusions = new List<MarketBoardTabPresentation.ClipRegion>();
        Assert.Throws<ArgumentNullException>(() =>
            MarketBoardTabPresentation.TryFillNativeOcclusions(100, null!, 32, occlusions));
        Assert.Throws<ArgumentNullException>(() =>
            MarketBoardTabPresentation.TryFillNativeOcclusions(100, [], 32, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MarketBoardTabPresentation.TryFillNativeOcclusions(100, [], -1, occlusions));
    }
    [Fact]
    public void OverlapUsesHalfOpenEdgesAndRejectsEachZeroDimension()
    {
        var position = new Vector2(10f, 10f);
        var size = new Vector2(20f, 20f);

        Assert.False(MarketBoardTabPresentation.Overlaps(position, Vector2.Zero, position, size));
        Assert.False(MarketBoardTabPresentation.Overlaps(position, new Vector2(0f, 20f), position, size));
        Assert.False(MarketBoardTabPresentation.Overlaps(position, new Vector2(20f, 0f), position, size));
        Assert.False(MarketBoardTabPresentation.Overlaps(position, size, position, new Vector2(0f, 20f)));
        Assert.False(MarketBoardTabPresentation.Overlaps(position, size, position, new Vector2(20f, 0f)));
        Assert.False(MarketBoardTabPresentation.Overlaps(
            position, size, new Vector2(20f, 20f), new Vector2(0f, 10f)));
        Assert.False(MarketBoardTabPresentation.Overlaps(
            position, size, new Vector2(20f, 20f), new Vector2(10f, 0f)));
        Assert.False(MarketBoardTabPresentation.Overlaps(position, size, new Vector2(30f, 10f), size));
        Assert.False(MarketBoardTabPresentation.Overlaps(position, size, new Vector2(-10f, 10f), size));
        Assert.False(MarketBoardTabPresentation.Overlaps(position, size, new Vector2(10f, 30f), size));
        Assert.False(MarketBoardTabPresentation.Overlaps(position, size, new Vector2(10f, -10f), size));
        Assert.True(MarketBoardTabPresentation.Overlaps(position, size, new Vector2(29.5f, 29.5f), size));
    }

    [Fact]
    public void ContainsUsesHalfOpenEdgesAndRejectsEachZeroDimension()
    {
        var position = new Vector2(10f, 10f);
        var size = new Vector2(20f, 20f);

        Assert.False(MarketBoardTabPresentation.Contains(position, new Vector2(0f, 20f), position));
        Assert.False(MarketBoardTabPresentation.Contains(position, new Vector2(20f, 0f), position));
        Assert.True(MarketBoardTabPresentation.Contains(position, size, position));
        Assert.True(MarketBoardTabPresentation.Contains(position, size, new Vector2(29.5f, 29.5f)));
        Assert.False(MarketBoardTabPresentation.Contains(position, size, new Vector2(30f, 20f)));
        Assert.False(MarketBoardTabPresentation.Contains(position, size, new Vector2(20f, 30f)));
        Assert.False(MarketBoardTabPresentation.Contains(position, size, new Vector2(9.5f, 20f)));
        Assert.False(MarketBoardTabPresentation.Contains(position, size, new Vector2(20f, 9.5f)));
    }

    [Fact]
    public void CenterOcclusionProducesFourExactRegions()
    {
        var regions = MarketBoardTabPresentation.GetVisibleRegions(
            Vector2.Zero,
            new Vector2(100f, 100f),
            new Vector2(25f, 25f),
            new Vector2(50f, 50f));

        Assert.Equal(
            [
                new MarketBoardTabPresentation.ClipRegion(Vector2.Zero, new Vector2(100f, 25f)),
                new MarketBoardTabPresentation.ClipRegion(new Vector2(0f, 75f), new Vector2(100f, 100f)),
                new MarketBoardTabPresentation.ClipRegion(new Vector2(0f, 25f), new Vector2(25f, 75f)),
                new MarketBoardTabPresentation.ClipRegion(new Vector2(75f, 25f), new Vector2(100f, 75f)),
            ],
            regions);
    }

    [Fact]
    public void ReusableVisibleRegionBuffersProduceTheSameExactSubtraction()
    {
        var regions = new List<MarketBoardTabPresentation.ClipRegion>();
        var scratch = new List<MarketBoardTabPresentation.ClipRegion>();
        var occlusions = new[]
        {
            new MarketBoardTabPresentation.ClipRegion(
                new Vector2(25f, 25f),
                new Vector2(75f, 75f)),
        };

        MarketBoardTabPresentation.FillVisibleRegions(
            Vector2.Zero,
            new Vector2(100f, 100f),
            occlusions,
            regions,
            scratch);

        Assert.Equal(
            MarketBoardTabPresentation.GetVisibleRegions(
                Vector2.Zero,
                new Vector2(100f, 100f),
                occlusions),
            regions);
        var sameBufferException = Assert.Throws<ArgumentException>(() =>
            MarketBoardTabPresentation.FillVisibleRegions(
                Vector2.Zero,
                Vector2.One,
                occlusions,
                regions,
                regions));
        Assert.Equal("scratch", sameBufferException.ParamName);
        Assert.Contains("must be distinct", sameBufferException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReusedBuffersMatchIndependentPixelCoverageAcrossChangingMaximumOcclusionFrames()
    {
        var random = new Random(7_718);
        var regions = new List<MarketBoardTabPresentation.ClipRegion>();
        var scratch = new List<MarketBoardTabPresentation.ClipRegion>();
        for (var frame = 0; frame < 96; frame++)
        {
            var origin = new Vector2(frame - 48, 24 - frame);
            var occlusions = new List<MarketBoardTabPresentation.ClipRegion>();
            if (frame % 3 == 0)
            {
                for (var index = 0; index < 32; index++)
                {
                    var minimum = origin + new Vector2(random.Next(-4, 20), random.Next(-4, 20));
                    occlusions.Add(new(minimum, minimum + new Vector2(random.Next(1, 9), random.Next(1, 9))));
                }
            }
            else if (frame % 3 == 1)
            {
                occlusions.Add(new(origin, origin + new Vector2(16)));
            }

            MarketBoardTabPresentation.FillVisibleRegions(origin, new(16), occlusions, regions, scratch);

            // Integer-aligned rectangles let cell centers be an independent exact
            // coverage oracle, including duplicate draw regions and stale buffers.
            for (var y = 0; y < 16; y++)
            {
                for (var x = 0; x < 16; x++)
                {
                    var point = origin + new Vector2(x + 0.5f, y + 0.5f);
                    var covered = occlusions.Any(region => Covers(region, point));
                    Assert.Equal(covered ? 0 : 1, regions.Count(region => Covers(region, point)));
                }
            }

            Assert.All(regions, region =>
            {
                Assert.True(region.Minimum.X >= origin.X && region.Minimum.Y >= origin.Y);
                Assert.True(region.Maximum.X <= origin.X + 16 && region.Maximum.Y <= origin.Y + 16);
                Assert.True(region.Maximum.X > region.Minimum.X && region.Maximum.Y > region.Minimum.Y);
            });
        }

        static bool Covers(MarketBoardTabPresentation.ClipRegion region, Vector2 point)
            => point.X >= region.Minimum.X && point.X < region.Maximum.X
                && point.Y >= region.Minimum.Y && point.Y < region.Maximum.Y;
    }

    [Fact]
    public void ReusableVisibleRegionBuffersRequireEveryInputAndClearPreviousState()
    {
        var regions = new List<MarketBoardTabPresentation.ClipRegion>
        {
            new(Vector2.Zero, Vector2.One),
        };
        var scratch = new List<MarketBoardTabPresentation.ClipRegion>
        {
            new(Vector2.Zero, Vector2.One),
        };

        var missingOcclusions = Assert.Throws<ArgumentNullException>(() =>
            MarketBoardTabPresentation.FillVisibleRegions(
                Vector2.Zero,
                Vector2.One,
                null!,
                regions,
                scratch));
        var missingRegions = Assert.Throws<ArgumentNullException>(() =>
            MarketBoardTabPresentation.FillVisibleRegions(
                Vector2.Zero,
                Vector2.One,
                [],
                null!,
                scratch));
        var missingScratch = Assert.Throws<ArgumentNullException>(() =>
            MarketBoardTabPresentation.FillVisibleRegions(
                Vector2.Zero,
                Vector2.One,
                [],
                regions,
                null!));

        Assert.Equal("occlusions", missingOcclusions.ParamName);
        Assert.Equal("regions", missingRegions.ParamName);
        Assert.Equal("scratch", missingScratch.ParamName);

        MarketBoardTabPresentation.FillVisibleRegions(
            Vector2.Zero,
            Vector2.Zero,
            [],
            regions,
            scratch);

        Assert.Empty(regions);
        Assert.Empty(scratch);
    }

    [Fact]
    public void MultipleForegroundWindowsClipEveryCoveredRailRegion()
    {
        var occlusions = new[]
        {
            new MarketBoardTabPresentation.ClipRegion(
                Vector2.Zero,
                new Vector2(25f, 100f)),
            new MarketBoardTabPresentation.ClipRegion(
                new Vector2(25f, 75f),
                new Vector2(100f, 100f)),
        };

        var regions = MarketBoardTabPresentation.GetVisibleRegions(
            Vector2.Zero,
            new Vector2(100f, 100f),
            occlusions);

        Assert.Equal(
            new MarketBoardTabPresentation.ClipRegion(
                new Vector2(25f, 0f),
                new Vector2(100f, 75f)),
            Assert.Single(regions));
    }

    [Fact]
    public void NativeOcclusionDeterminesPointerOwnershipAcrossEveryForegroundWindow()
    {
        var occlusions = new[]
        {
            new MarketBoardTabPresentation.ClipRegion(
                new Vector2(10f, 10f),
                new Vector2(30f, 30f)),
            new MarketBoardTabPresentation.ClipRegion(
                new Vector2(40f, 40f),
                new Vector2(60f, 60f)),
        };

        Assert.True(MarketBoardTabPresentation.IsPointOccluded(new Vector2(10f, 10f), occlusions));
        Assert.True(MarketBoardTabPresentation.IsPointOccluded(new Vector2(59f, 59f), occlusions));
        Assert.False(MarketBoardTabPresentation.IsPointOccluded(new Vector2(35f, 35f), occlusions));
        Assert.False(MarketBoardTabPresentation.IsPointOccluded(new Vector2(60f, 60f), occlusions));
    }

    [Fact]
    public void NativeOcclusionCollectionsAreRequired()
    {
        var visibleException = Assert.Throws<ArgumentNullException>(() =>
            MarketBoardTabPresentation.GetVisibleRegions(
                Vector2.Zero,
                Vector2.One,
                (IReadOnlyList<MarketBoardTabPresentation.ClipRegion>)null!));
        var pointerException = Assert.Throws<ArgumentNullException>(() =>
            MarketBoardTabPresentation.IsPointOccluded(
                Vector2.Zero,
                null!));

        Assert.Equal("occlusions", visibleException.ParamName);
        Assert.Equal("occlusions", pointerException.ParamName);
    }

    [Theory]
    [InlineData(0.75f)]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void CompanionMatchesVisibleFrameInsetsAtNativeScale(float scale)
    {
        var placement = MarketBoardTabPresentation.GetLockedCompanionPlacement(
            marketBoardPosition: new Vector2(120f, 45f),
            marketBoardSize: new Vector2(880f, 756f) * scale,
            gap: 8f);

        Assert.Equal(new Vector2(128f + (880f * scale), 45f + (4f * scale)), placement.Position);
        Assert.Equal(new Vector2(880f, 736f) * scale, placement.Size);
        Assert.Equal(45f + (740f * scale), placement.Position.Y + placement.Size.Y);
    }

    [Fact]
    public void GeneratedGeometryMaintainsSymmetryContainmentAndVisibleRegionBounds()
    {
        var random = new Random(20260901);
        for (var iteration = 0; iteration < 512; iteration++)
        {
            var position = new Vector2(random.Next(-100, 100), random.Next(-100, 100));
            var size = new Vector2(random.Next(1, 100), random.Next(1, 100));
            var otherPosition = new Vector2(random.Next(-100, 100), random.Next(-100, 100));
            var otherSize = new Vector2(random.Next(1, 100), random.Next(1, 100));

            Assert.Equal(
                MarketBoardTabPresentation.Overlaps(position, size, otherPosition, otherSize),
                MarketBoardTabPresentation.Overlaps(otherPosition, otherSize, position, size));
            Assert.True(MarketBoardTabPresentation.Contains(position, size, position));
            Assert.False(MarketBoardTabPresentation.Contains(position, size, position + size));

            var regions = MarketBoardTabPresentation.GetVisibleRegions(
                position, size, otherPosition, otherSize);
            Assert.All(regions, region =>
            {
                var regionSize = region.Maximum - region.Minimum;
                Assert.True(regionSize.X > 0f && regionSize.Y > 0f);
                Assert.True(region.Minimum.X >= position.X && region.Minimum.Y >= position.Y);
                Assert.True(region.Maximum.X <= position.X + size.X);
                Assert.True(region.Maximum.Y <= position.Y + size.Y);
                Assert.False(MarketBoardTabPresentation.Overlaps(
                    region.Minimum, regionSize, otherPosition, otherSize));
            });

            var thirdPosition = new Vector2(random.Next(-100, 100), random.Next(-100, 100));
            var thirdSize = new Vector2(random.Next(1, 100), random.Next(1, 100));
            var occlusions = new[]
            {
                new MarketBoardTabPresentation.ClipRegion(
                    otherPosition,
                    otherPosition + otherSize),
                new MarketBoardTabPresentation.ClipRegion(
                    thirdPosition,
                    thirdPosition + thirdSize),
            };
            var multiplyClippedRegions = MarketBoardTabPresentation.GetVisibleRegions(
                position,
                size,
                occlusions);
            Assert.All(multiplyClippedRegions, region =>
            {
                var regionSize = region.Maximum - region.Minimum;
                Assert.DoesNotContain(occlusions, occlusion =>
                    MarketBoardTabPresentation.Overlaps(
                        region.Minimum,
                        regionSize,
                        occlusion.Minimum,
                        occlusion.Maximum - occlusion.Minimum));
            });
        }
    }


    /// <summary>
    /// Verifies native byte alpha is converted to the ImGui opacity range.
    /// </summary>
    [Theory]
    [InlineData((byte)0, 0f)]
    [InlineData((byte)128, 0.5019608f)]
    [InlineData((byte)255, 1f)]
    public void NativeAlphaControlsOpacity(byte nativeAlpha, float expected)
    {
        // Arrange

        // Act
        var actual = MarketBoardTabPresentation.GetOpacity(nativeAlpha, false, false);

        // Assert
        Assert.Equal(expected, actual, precision: 6);
    }

    /// <summary>
    /// Verifies modal suppression hides the overlay regardless of native alpha.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ModalVisibilitySuppressionHidesTabs(bool current, bool applied)
    {
        // Arrange

        // Act
        var opacity = MarketBoardTabPresentation.GetOpacity(255, current, applied);

        // Assert
        Assert.Equal(0f, opacity);
    }

    /// <summary>
    /// Verifies a native Search Results window covering the rail is detected.
    /// </summary>
    [Fact]
    public void SearchResultsCoveringRailIsDetected()
    {
        // Arrange
        var railPosition = new Vector2(100f, 100f);
        var railSize = new Vector2(98f, 875f);
        var resultsPosition = new Vector2(50f, 400f);
        var resultsSize = new Vector2(800f, 500f);

        // Act
        var overlaps = MarketBoardTabPresentation.Overlaps(
            railPosition,
            railSize,
            resultsPosition,
            resultsSize);

        // Assert
        Assert.True(overlaps);
    }

    /// <summary>
    /// Verifies Search Results overlaps only the lower tab in this layout.
    /// </summary>
    [Fact]
    public void SearchResultsOccludesLowerTabButLeavesUpperTabVisible()
    {
        // Arrange
        var upperTabPosition = new Vector2(100f, 100f);
        var lowerTabPosition = new Vector2(100f, 450f);
        var tabSize = new Vector2(98f, 74f);
        var resultsPosition = new Vector2(50f, 400f);
        var resultsSize = new Vector2(800f, 500f);

        // Act
        var upperTabOccluded = MarketBoardTabPresentation.Overlaps(
            upperTabPosition,
            tabSize,
            resultsPosition,
            resultsSize);
        var lowerTabOccluded = MarketBoardTabPresentation.Overlaps(
            lowerTabPosition,
            tabSize,
            resultsPosition,
            resultsSize);

        // Assert
        Assert.False(upperTabOccluded);
        Assert.True(lowerTabOccluded);
    }

    /// <summary>
    /// Verifies a partially covered tab keeps the exact area outside Search Results visible.
    /// </summary>
    [Fact]
    public void PartialSearchResultsOverlapProducesVisibleTabRegions()
    {
        // Arrange
        var tabPosition = new Vector2(100f, 100f);
        var tabSize = new Vector2(98f, 74f);
        var resultsPosition = new Vector2(150f, 120f);
        var resultsSize = new Vector2(800f, 500f);

        // Act
        var regions = MarketBoardTabPresentation.GetVisibleRegions(
            tabPosition,
            tabSize,
            resultsPosition,
            resultsSize);

        // Assert
        Assert.Equal(2, regions.Count);
        Assert.Contains(
            new MarketBoardTabPresentation.ClipRegion(
                new Vector2(100f, 100f),
                new Vector2(198f, 120f)),
            regions);
        Assert.Contains(
            new MarketBoardTabPresentation.ClipRegion(
                new Vector2(100f, 120f),
                new Vector2(150f, 174f)),
            regions);
    }

    /// <summary>
    /// Verifies a tab fully behind Search Results has no visible drawing regions.
    /// </summary>
    [Fact]
    public void FullSearchResultsOverlapProducesNoVisibleTabRegions()
    {
        // Arrange
        var tabPosition = new Vector2(100f, 100f);
        var tabSize = new Vector2(98f, 74f);
        var resultsPosition = new Vector2(50f, 50f);
        var resultsSize = new Vector2(800f, 500f);

        // Act
        var regions = MarketBoardTabPresentation.GetVisibleRegions(
            tabPosition,
            tabSize,
            resultsPosition,
            resultsSize);

        // Assert
        Assert.Empty(regions);
    }

    /// <summary>
    /// Verifies an unobstructed tab retains one complete visible region.
    /// </summary>
    [Fact]
    public void UnobstructedTabProducesCompleteVisibleRegion()
    {
        // Arrange
        var tabPosition = new Vector2(100f, 100f);
        var tabSize = new Vector2(98f, 74f);

        // Act
        var regions = MarketBoardTabPresentation.GetVisibleRegions(
            tabPosition,
            tabSize,
            new Vector2(500f, 500f),
            new Vector2(100f, 100f));

        // Assert
        Assert.Equal(
            new MarketBoardTabPresentation.ClipRegion(tabPosition, tabPosition + tabSize),
            Assert.Single(regions));
    }

    /// <summary>
    /// Verifies invalid tab bounds never produce a drawable region.
    /// </summary>
    [Theory]
    [InlineData(0f, 74f)]
    [InlineData(98f, 0f)]
    public void InvalidTabBoundsProduceNoVisibleRegions(float width, float height)
    {
        // Arrange

        // Act
        var regions = MarketBoardTabPresentation.GetVisibleRegions(
            new Vector2(100f, 100f),
            new Vector2(width, height),
            new Vector2(500f, 500f),
            new Vector2(100f, 100f));

        // Assert
        Assert.Empty(regions);
    }

    /// <summary>
    /// Verifies Search Results owns input across its native screen rectangle.
    /// </summary>
    [Theory]
    [InlineData(100f, 100f, true)]
    [InlineData(899.9f, 599.9f, true)]
    [InlineData(900f, 600f, false)]
    [InlineData(99.9f, 100f, false)]
    public void SearchResultsBoundsDetermineInputOcclusion(float x, float y, bool expected)
    {
        // Arrange
        var resultsPosition = new Vector2(100f, 100f);
        var resultsSize = new Vector2(800f, 500f);

        // Act
        var contains = MarketBoardTabPresentation.Contains(
            resultsPosition,
            resultsSize,
            new Vector2(x, y));

        // Assert
        Assert.Equal(expected, contains);
    }

    /// <summary>
    /// Verifies invalid native bounds never claim pointer ownership.
    /// </summary>
    [Theory]
    [InlineData(0f, 500f)]
    [InlineData(800f, 0f)]
    public void InvalidBoundsDoNotContainPointer(float width, float height)
    {
        // Arrange

        // Act
        var contains = MarketBoardTabPresentation.Contains(
            new Vector2(100f, 100f),
            new Vector2(width, height),
            new Vector2(100f, 100f));

        // Assert
        Assert.False(contains);
    }

    /// <summary>
    /// Verifies windows merely touching the rail edge do not count as covering it.
    /// </summary>
    [Fact]
    public void SearchResultsTouchingRailEdgeDoesNotOverlap()
    {
        // Arrange
        var railPosition = new Vector2(100f, 100f);
        var railSize = new Vector2(98f, 875f);
        var resultsPosition = new Vector2(198f, 100f);
        var resultsSize = new Vector2(800f, 500f);

        // Act
        var overlaps = MarketBoardTabPresentation.Overlaps(
            railPosition,
            railSize,
            resultsPosition,
            resultsSize);

        // Assert
        Assert.False(overlaps);
    }

    /// <summary>
    /// Verifies invalid native bounds never suppress the rail.
    /// </summary>
    [Theory]
    [InlineData(0f, 875f)]
    [InlineData(98f, 0f)]
    [InlineData(-1f, 875f)]
    public void InvalidBoundsDoNotOverlap(float width, float height)
    {
        // Arrange
        var railPosition = new Vector2(100f, 100f);
        var railSize = new Vector2(width, height);
        var resultsPosition = new Vector2(50f, 50f);
        var resultsSize = new Vector2(800f, 500f);

        // Act
        var overlaps = MarketBoardTabPresentation.Overlaps(
            railPosition,
            railSize,
            resultsPosition,
            resultsSize);

        // Assert
        Assert.False(overlaps);
    }
}

