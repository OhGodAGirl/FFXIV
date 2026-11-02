using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using EZDelivery.Features.Interface;

namespace EZDelivery.HostTests;

public sealed unsafe class NativeImGuiTests
{
    private static readonly Type Theme = typeof(InterfaceRenderer).Assembly.GetType("EZDelivery.Features.Interface.PluginTheme", true)!;

    public static IEnumerable<object[]> ThemesAndStyles()
    {
        for (uint theme = 0; theme < 8; theme++)
            foreach (var style in Enum.GetValues<ShopTabStyle>()) yield return [theme, style];
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(4u)]
    [InlineData(5u)]
    [InlineData(6u)]
    [InlineData(7u)]
    public void ActualThemePushBindsEveryNativeColorAndPopRestoresEntireStyle(uint theme)
    {
        // Arrange
        using var gui = new HeadlessImGui();
        SetTheme(theme);
        ImGui.NewFrame();
        var baseline = StyleBytes();
        var roles = Theme.Assembly.GetType("EZDelivery.Features.Interface.ThemeColorRole", true)!;
        var colors = Theme.Assembly.GetType("EZDelivery.Features.Interface.ThemeColors", true)!;
        var resolve = colors.GetMethod("Resolve", BindingFlags.Static | BindingFlags.NonPublic)!;

        // Act
        ThemeMethod("Push");
        try
        {
            // Assert bindings while the real native style stack is active.
            foreach (var role in Enum.GetValues(roles))
            {
                var slot = Enum.Parse<ImGuiCol>(role.ToString()!);
                var expected = (Vector4)resolve.Invoke(null, [theme, role])!;
                Assert.Equal(expected, ImGui.GetStyle().Colors[(int)slot]);
            }
        }
        finally { ThemeMethod("Pop"); }

        Assert.Equal(baseline, StyleBytes());
        ImGui.Render();
    }

    [Theory]
    [MemberData(nameof(ThemesAndStyles))]
    public void ActualRendererDrawsAllSurfacesAndRestoresStyleAcrossThemesAndTabs(uint theme, ShopTabStyle tabStyle)
    {
        // Arrange
        using var gui = new HeadlessImGui();
        SetTheme(theme);
        var facade = new InterfaceFacade { SettingsOpen = true, DiagnosticsOpen = true };
        using var renderer = new InterfaceRenderer(facade);
        var baseline = StyleBytes();

        // Act: cover each category with actual renderer, native widgets, paths and font vertices.
        foreach (var category in Enum.GetValues<ShopCategory>())
        {
            var rows = new[]
            {
                new ShopRow("one", 1, 0, "Available collection item", category, 100, true, ""),
                new ShopRow("two", 2, 0, "Unavailable collection item", category, 200, false, "Unavailable"),
            };
            var state = State(tabStyle, rows);
            facade.Prepare(state);
            facade.HandlePresentationIntent(new SelectCategoryIntent(7, category));
            for (var frame = 0; frame < 2; frame++)
            {
                ImGui.NewFrame();
                renderer.Draw();
                Assert.Equal(baseline, StyleBytes());
                ImGui.Render();
            }

            // Assert
            Assert.True(ImGui.GetDrawData().TotalVtxCount > 100);
            Assert.True(ImGui.GetDrawData().TotalIdxCount > 100);
            Assert.Empty(facade.DrainIntents());
        }
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(4u)]
    [InlineData(7u)]
    public void LightThemeRowsAndMissingTexturePlaceholdersUseTheTransformedColors(uint theme)
    {
        // Arrange
        using var gui = new HeadlessImGui();
        SetTheme(theme);
        var facade = new InterfaceFacade();
        using var renderer = new InterfaceRenderer(facade);
        var rows = Enumerable.Range(0, 3).Select(index => new ShopRow(index.ToString(), (uint)(index + 1), 0,
            "Item " + index, ShopCategory.Weapons, 100, true, "")).ToArray();
        var state = State(ShopTabStyle.RoundTabs, rows);
        facade.Prepare(state with { Shopping = state.Shopping with { Running = true, CurrentOfferKey = "2" } });
        facade.HandlePresentationIntent(new SelectCategoryIntent(7, ShopCategory.Weapons));

        // Act
        for (var frame = 0; frame < 2; frame++)
        {
            ImGui.NewFrame();
            renderer.Draw();
            ImGui.Render();
        }
        var vertexColors = VertexColors();

        // Assert actual generated draw vertices, rather than merely checking a managed palette.
        foreach (var original in new[] { new Vector4(.34f, .32f, .25f, 1), new Vector4(.255f, .263f, .267f, 1), new Vector4(.20f, .21f, .21f, 1) })
        {
            var expected = (Vector4)Theme.GetMethod("Themed", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [original])!;
            Assert.Contains(ImGui.ColorConvertFloat4ToU32(expected), vertexColors);
        }
    }

    [Fact]
    public void InstalledNativeImGuiBuildsAHeadlessFrame()
    {
        // Arrange
        using var gui = new HeadlessImGui();

        // Act
        ImGui.NewFrame();
        ImGui.Begin("Standalone native integration");
        ImGui.TextUnformatted("No game process or inputs.");
        ImGui.End();
        ImGui.Render();
        ImGui.NewFrame();
        ImGui.Begin("Standalone native integration");
        ImGui.TextUnformatted("No game process or inputs.");
        ImGui.End();
        ImGui.Render();

        // Assert
        Assert.True(ImGui.GetDrawData().TotalVtxCount > 0);
        Assert.True(ImGui.GetDrawData().TotalIdxCount > 0);
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var loaded = Assert.Single(process.Modules.Cast<System.Diagnostics.ProcessModule>(),
            module => string.Equals(module.ModuleName, "cimgui.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "cimgui.dll")), Path.GetFullPath(loaded.FileName), true);
    }

    [Theory]
    [InlineData(false, true, false, "StartDeliveryIntent")]
    [InlineData(true, false, true, "StopDeliveryIntent")]
    [InlineData(false, false, false, null)]
    [InlineData(false, true, true, null)]
    public void ActualDeliveryButtonRoutesEnabledClicksAndRejectsDisabledClicks(bool running, bool canStart, bool busy, string? expected)
    {
        // Arrange
        using var gui = new HeadlessImGui();
        var facade = new InterfaceFacade();
        using var renderer = new InterfaceRenderer(facade);
        facade.Prepare(State(ShopTabStyle.RoundTabs, []) with
        {
            Delivery = new(running, canStart, "Ready", 0),
            AnyOperationRunning = busy,
        });
        DrawFrame(renderer);
        DrawFrame(renderer);

        // Act: inject events into this isolated ImGui context, never into the OS or game.
        Click(renderer, new Vector2(1470, 865));
        var intents = facade.DrainIntents();

        // Assert
        if (expected is null) Assert.Empty(intents);
        else Assert.Equal(expected, Assert.Single(intents).GetType().Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualCategoryButtonRespectsAnOccludingImGuiWindow(bool occluded)
    {
        // Arrange
        using var gui = new HeadlessImGui();
        var facade = new InterfaceFacade();
        using var renderer = new InterfaceRenderer(facade);
        facade.Prepare(State(ShopTabStyle.RoundTabs, []));
        Action? overlay = occluded ? () =>
        {
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(new Vector2(200, 400));
            ImGui.Begin("Occluding test window", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings);
            ImGui.TextUnformatted("Owns this input");
            ImGui.End();
        } : null;
        DrawFrame(renderer, overlay);
        DrawFrame(renderer, overlay);

        // Act
        Click(renderer, new Vector2(70, 132), overlay);
        var intents = facade.DrainIntents();

        // Assert
        if (occluded) Assert.Empty(intents);
        else Assert.Equal(ShopCategory.Weapons, Assert.IsType<SelectCategoryIntent>(Assert.Single(intents)).Category);
    }

    internal sealed class HeadlessImGui : IDisposable
    {
        private readonly ImGuiContextPtr context;

        public HeadlessImGui()
        {
            context = ImGui.CreateContext();
            var io = ImGui.GetIO();
            io.DisplaySize = new Vector2(2400, 1400);
            io.DeltaTime = 1f / 60;
            io.IniFilename = null;
            io.Fonts.AddFontDefault();
            Assert.True(io.Fonts.Build());
        }

        public void Dispose()
        {
            SetTheme(0);
            ImGui.DestroyContext(context);
        }
    }

    private static UiState State(ShopTabStyle style, IReadOnlyList<ShopRow> rows) => UiState.Empty with
    {
        Generation = 7,
        LoggedIn = true,
        DeliveryAnchor = new(new(1500, 800, 700, 500), true),
        ShopAnchor = new(new(100, 100, 700, 900), true),
        ExpertDeliverySelected = true,
        Delivery = new(false, true, "Ready", 0),
        Shopping = new(false, true, "Collection ready", rows, 1000, "Ready", 0),
        ShopTabStyle = style,
        DiagnosticsReport = "{\"Source\":\"headless integration\"}",
    };

    private static byte[] StyleBytes() => new ReadOnlySpan<byte>(ImGui.GetStyle().Handle, sizeof(ImGuiStyle)).ToArray();

    private static void Click(InterfaceRenderer renderer, Vector2 point, Action? afterDraw = null)
    {
        var io = ImGui.GetIO();
        io.AddMousePosEvent(point.X, point.Y);
        DrawFrame(renderer, afterDraw);
        io.AddMouseButtonEvent(0, true);
        DrawFrame(renderer, afterDraw);
        io.AddMouseButtonEvent(0, false);
        DrawFrame(renderer, afterDraw);
    }

    private static void DrawFrame(InterfaceRenderer renderer, Action? afterDraw = null)
    {
        ImGui.NewFrame();
        renderer.Draw();
        afterDraw?.Invoke();
        ImGui.Render();
    }

    private static HashSet<uint> VertexColors()
    {
        var data = ImGui.GetDrawData();
        var colors = new HashSet<uint>();
        for (var index = 0; index < data.CmdListsCount; index++)
        {
            var list = new ImDrawListPtr(data.CmdLists[index]);
            for (var vertex = 0; vertex < list.VtxBuffer.Size; vertex++) colors.Add(list.VtxBuffer[vertex].Col);
        }
        return colors;
    }

    private static void SetTheme(uint theme) => Theme.GetMethod("SetGameTheme", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [theme]);
    private static void ThemeMethod(string name) => Theme.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
}
