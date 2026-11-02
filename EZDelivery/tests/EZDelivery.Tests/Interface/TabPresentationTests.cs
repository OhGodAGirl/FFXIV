using System.Numerics;
using EZDelivery.Features.Interface;

namespace EZDelivery.Tests.Interface;

public sealed class TabPresentationTests
{
    private const uint Fill = 11, Border = 22, Inset = 33;

    [Theory]
    [InlineData(ShopTabStyle.InsetSockets, "Inset Sockets")]
    [InlineData(ShopTabStyle.FlushIndex, "Flush Index")]
    [InlineData(ShopTabStyle.RibbonTabs, "Ribbon Tabs")]
    [InlineData(ShopTabStyle.RoundTabs, "Round Tabs")]
    [InlineData(ShopTabStyle.BeveledIndex, "Beveled Index")]
    public void SupportedStylesKeepTheirIdentityAndPlayerFacingName(ShopTabStyle style, string name)
    {
        // Arrange / Act
        var normalized = TabPresentation.Normalize(style);

        // Assert
        Assert.True(TabPresentation.IsSupported(style));
        Assert.Equal(style, normalized);
        Assert.Equal(name, TabPresentation.Name(style));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(int.MaxValue)]
    public void InvalidPersistedStylesUseTheDefaultShapeAndName(int stored)
    {
        // Arrange
        var invalid = (ShopTabStyle)stored;

        // Act
        var normalized = TabPresentation.Normalize(invalid);
        var actual = Draw(invalid);

        // Assert
        Assert.False(TabPresentation.IsSupported(invalid));
        Assert.Equal(ShopTabStyle.InsetSockets, normalized);
        Assert.Equal("Inset Sockets", TabPresentation.Name(invalid));
        Assert.Equal(Draw(normalized).Select(Describe), actual.Select(Describe));
    }

    [Fact]
    public void StyleIntentsAreQueuedWithoutChangingSelectionOrStartingTransactions()
    {
        // Arrange
        var facade = new InterfaceFacade();
        var state = UiState.Empty with { Generation = 19, ShopTabStyle = ShopTabStyle.RoundTabs };
        facade.Prepare(state);
        var intent = new ShopTabStyleIntent(19, ShopTabStyle.BeveledIndex);

        // Act
        var handled = facade.HandlePresentationIntent(intent);
        facade.Enqueue(intent);
        facade.Enqueue(new StartDeliveryIntent(19));
        var queued = facade.DrainIntents();

        // Assert
        Assert.False(handled);
        Assert.Null(facade.SelectedCategory);
        Assert.Equal(ShopTabStyle.InsetSockets, UiState.Empty.ShopTabStyle);
        Assert.Equal(ShopTabStyle.RoundTabs, facade.State.ShopTabStyle);
        Assert.Collection(queued,
            first =>
            {
                var style = Assert.IsType<ShopTabStyleIntent>(first);
                Assert.Equal(19, style.Generation);
                Assert.Equal(ShopTabStyle.BeveledIndex, style.Style);
            },
            second => Assert.IsType<StartDeliveryIntent>(second));
    }

    public static IEnumerable<object[]> ShapesAndScales()
    {
        foreach (var style in Enum.GetValues<ShopTabStyle>())
            foreach (var scale in new[] { .5f, 1, 1.25f, 1.5f, 2, 4 })
                yield return [style, scale];
    }

    [Theory]
    [MemberData(nameof(ShapesAndScales))]
    public void EveryShapeStaysWithinItsTabAndBalancesClipState(ShopTabStyle style, float scale)
    {
        // Arrange
        var min = new Vector2(-83, 117);
        var max = min + new Vector2(48, 46) * scale;
        var canvas = new RecordingCanvas();

        // Act
        TabPresentation.Draw(ref canvas, min, max, scale, style, Fill, Border, Inset);

        // Assert
        Assert.NotEmpty(canvas.Commands);
        var clipDepth = 0;
        foreach (var command in canvas.Commands)
        {
            if (command.Kind == "PushClip")
            {
                clipDepth++;
                Assert.Equal(min - Vector2.One, command.Points[0]);
                Assert.Equal(max + Vector2.One, command.Points[1]);
                continue;
            }
            if (command.Kind == "PopClip")
            {
                Assert.Equal(1, clipDepth--);
                continue;
            }
            foreach (var point in command.Points)
            {
                Assert.InRange(point.X, min.X, max.X);
                Assert.InRange(point.Y, min.Y, max.Y);
            }
            if (command.Kind is "Circle" or "Arc")
            {
                var center = command.Points[0];
                Assert.InRange(center.X - command.Size, min.X, max.X);
                Assert.InRange(center.X + command.Size, min.X, max.X);
                Assert.Equal(min.Y, center.Y - command.Size);
                Assert.Equal(max.Y, center.Y + command.Size);
            }
            if (command.Kind is "FillRect" or "Circle" or "Triangle")
                Assert.Contains(command.Color, new[] { Fill, Inset });
            if (command.Kind is "OutlineRect" or "StrokeOpen") Assert.Equal(Border, command.Color);
        }
        Assert.Equal(0, clipDepth);
    }

    [Theory]
    [MemberData(nameof(ShapesAndScales))]
    public void EveryShapeScalesItsGeometryRoundingAndStrokeWidthTogether(ShopTabStyle style, float scale)
    {
        var original = Draw(style);
        var canvas = new RecordingCanvas();
        TabPresentation.Draw(ref canvas, Vector2.Zero, new Vector2(48, 46) * scale, scale, style, Fill, Border, Inset);
        Assert.Equal(original.Count, canvas.Commands.Count);
        for (var i = 0; i < original.Count; i++)
        {
            var expected = original[i];
            var actual = canvas.Commands[i];
            Assert.Equal(expected.Kind, actual.Kind);
            Assert.Equal(expected.Color, actual.Color);
            Assert.Equal(expected.LeftOnly, actual.LeftOnly);
            Assert.Equal(expected.Segments, actual.Segments);
            Assert.Equal(expected.Start, actual.Start);
            Assert.Equal(expected.End, actual.End);
            Assert.Equal(expected.Size * scale, actual.Size);
            if (expected.Kind == "PushClip") continue; // Deliberate one-pixel antialiasing margin.
            Assert.Equal(expected.Points.Select(point => point * scale), actual.Points);
        }
    }

    [Theory]
    [InlineData(.5f)]
    [InlineData(1f)]
    [InlineData(4f)]
    public void RoundOutlineConnectsBothHorizontalsThroughCurveAndLeavesFrameSeamOpen(float scale)
    {
        // Arrange
        var min = new Vector2(10, 20);
        var max = min + new Vector2(48, 46) * scale;
        var canvas = new RecordingCanvas();

        // Act
        TabPresentation.Draw(ref canvas, min, max, scale, ShopTabStyle.RoundTabs, Fill, Border, Inset);

        // Assert
        Assert.Equal(new[] { "Circle", "FillRect", "PushClip", "Point", "Arc", "Point", "StrokeOpen", "PopClip" },
            canvas.Commands.Select(command => command.Kind));
        Assert.Equal(new Vector2(max.X, max.Y), canvas.Commands[3].Points[0]);
        var arc = canvas.Commands[4];
        Assert.Equal(new Vector2(min.X + 23 * scale, min.Y + 23 * scale), arc.Points[0]);
        Assert.Equal(23 * scale, arc.Size);
        Assert.Equal(MathF.PI / 2, arc.Start);
        Assert.Equal(MathF.PI * 1.5f, arc.End);
        Assert.Equal(new Vector2(max.X, min.Y), canvas.Commands[5].Points[0]);
        Assert.Equal(1.5f * scale, canvas.Commands[6].Size);
        Assert.Equal(16, arc.Segments);
    }

    [Fact]
    public void InsetSocketsKeepNestedPanelsAndDistinctInsetColor()
    {
        // Arrange / Act
        var commands = Draw(ShopTabStyle.InsetSockets);

        // Assert
        Assert.Equal(new[] { "FillRect", "OutlineRect", "FillRect", "OutlineRect" }, commands.Select(command => command.Kind));
        Assert.Equal(Inset, commands[0].Color);
        Assert.Equal(Fill, commands[2].Color);
        Assert.Equal(new Vector2(3), commands[2].Points[0] - commands[0].Points[0]);
        Assert.Equal(new Vector2(3), commands[0].Points[1] - commands[2].Points[1]);
        Assert.Equal(commands[0].Points, commands[1].Points);
        Assert.Equal(commands[2].Points, commands[3].Points);
        Assert.Equal(5, commands[0].Size);
        Assert.Equal(3, commands[2].Size);
    }

    [Fact]
    public void FlushIndexRoundsOnlyOuterCornersAndPaintsOverItsInnerBorder()
    {
        // Arrange / Act
        var commands = Draw(ShopTabStyle.FlushIndex);

        // Assert
        Assert.Equal(3, commands.Count);
        Assert.True(commands[0].LeftOnly);
        Assert.True(commands[1].LeftOnly);
        Assert.Equal(3, commands[0].Size);
        var seam = commands[2];
        Assert.Equal("Line", seam.Kind);
        Assert.Equal(Fill, seam.Color);
        Assert.Equal(new[] { new Vector2(48, 1), new Vector2(48, 45) }, seam.Points);
        Assert.Equal(2, seam.Size);
    }

    [Theory]
    [InlineData(ShopTabStyle.RibbonTabs, 4)]
    [InlineData(ShopTabStyle.BeveledIndex, 5)]
    public void AngularStylesHaveConnectedOuterBordersWithoutRightEdge(ShopTabStyle style, int edgeCount)
    {
        // Arrange / Act
        var commands = Draw(style);

        // Assert
        var edges = commands.Where(command => command.Kind == "Line").ToArray();
        Assert.Equal(style == ShopTabStyle.RibbonTabs
                ? new[] { "FillRect", "Triangle", "Triangle" }
                : new[] { "FillRect", "FillRect", "Triangle", "Triangle" },
            commands.Where(command => command.Kind != "Line").Select(command => command.Kind));
        Assert.Equal(edgeCount, edges.Length);
        Assert.All(edges, edge => Assert.Equal(Border, edge.Color));
        Assert.DoesNotContain(edges, edge => edge.Points.All(point => point.X == 48));
        var endpoints = edges.SelectMany(edge => edge.Points).GroupBy(point => point).ToDictionary(group => group.Key, group => group.Count());
        Assert.Equal(1, endpoints[new(48, 0)]);
        Assert.Equal(1, endpoints[new(48, 46)]);
        Assert.All(endpoints.Where(pair => pair.Key.X != 48), pair => Assert.Equal(2, pair.Value));
        if (style == ShopTabStyle.RibbonTabs)
            Assert.Contains(new Vector2(5, 23), endpoints.Keys);
        else
        {
            Assert.Contains(new Vector2(0, 5), endpoints.Keys);
            Assert.Contains(new Vector2(0, 41), endpoints.Keys);
            Assert.DoesNotContain(Vector2.Zero, endpoints.Keys);
            Assert.DoesNotContain(new Vector2(0, 46), endpoints.Keys);
        }
    }

    [Theory]
    [InlineData(100f, 100f, 50f)]
    [InlineData(49f, 100f, 48f)]
    [InlineData(100f, 49f, 48f)]
    [InlineData(50f, 50f, 50f)]
    public void FrameBridgeRespectsViewportAndNativeBoundary(float viewportRight, float nativeLeft, float expectedWidth)
    {
        // Arrange
        var bookmark = new UiRect(0, 5, 48, 48);
        var viewport = new UiRect(0, 0, viewportRight, 100);
        var native = new UiRect(nativeLeft, 0, 600, 600);

        // Act
        var attached = ContextLayout.BridgeToFrame(bookmark, viewport, native, 1);

        // Assert
        Assert.Equal(bookmark with { Width = expectedWidth }, attached);
        Assert.True(attached.Right <= viewport.Right);
        Assert.False(attached.Intersects(native));
    }

    [Theory]
    [InlineData(.5f, 1f)]
    [InlineData(2f, 4f)]
    [InlineData(4f, 8f)]
    public void FrameBridgeUsesTwoLogicalUnitsAtTheHostScale(float scale, float expectedGap)
    {
        var bookmark = new UiRect(0, 5, 48 * scale, 48 * scale);
        var attached = ContextLayout.BridgeToFrame(bookmark, new(0, 0, 1000, 1000), new(500, 0, 400, 600), scale);
        Assert.Equal(bookmark with { Width = bookmark.Width + expectedGap }, attached);
    }

    private static List<Command> Draw(ShopTabStyle style)
    {
        var canvas = new RecordingCanvas();
        TabPresentation.Draw(ref canvas, Vector2.Zero, new Vector2(48, 46), 1, style, Fill, Border, Inset);
        return canvas.Commands;
    }

    private static string Describe(Command command) => $"{command.Kind}:{string.Join(';', command.Points)}:{command.Color}:{command.Size}:{command.LeftOnly}";

    private sealed record Command(string Kind, Vector2[] Points, uint Color = 0, float Size = 0,
        bool LeftOnly = false, float Start = 0, float End = 0, int Segments = 0);

    private sealed class RecordingCanvas : ITabCanvas
    {
        public List<Command> Commands { get; } = [];
        public void FillRect(Vector2 min, Vector2 max, uint color, float rounding = 0, bool leftCornersOnly = false)
            => Commands.Add(new("FillRect", [min, max], color, rounding, leftCornersOnly));
        public void OutlineRect(Vector2 min, Vector2 max, uint color, float rounding, bool leftCornersOnly = false)
            => Commands.Add(new("OutlineRect", [min, max], color, rounding, leftCornersOnly));
        public void FillCircle(Vector2 center, float radius, uint color, int segments) => Commands.Add(new("Circle", [center], color, radius, Segments: segments));
        public void FillTriangle(Vector2 first, Vector2 second, Vector2 third, uint color) => Commands.Add(new("Triangle", [first, second, third], color));
        public void Line(Vector2 first, Vector2 second, uint color, float thickness) => Commands.Add(new("Line", [first, second], color, thickness));
        public void PushClip(Vector2 min, Vector2 max) => Commands.Add(new("PushClip", [min, max]));
        public void PathPoint(Vector2 point) => Commands.Add(new("Point", [point]));
        public void PathArc(Vector2 center, float radius, float start, float end, int segments)
            => Commands.Add(new("Arc", [center], Size: radius, Start: start, End: end, Segments: segments));
        public void StrokeOpenPath(uint color, float thickness) => Commands.Add(new("StrokeOpen", [], color, thickness));
        public void PopClip() => Commands.Add(new("PopClip", []));
    }
}
