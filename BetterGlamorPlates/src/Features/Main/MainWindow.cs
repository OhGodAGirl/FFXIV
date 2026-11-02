using System.Numerics;
using BetterGlamorPlates.Bootstrap;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Shared.Operations;
using BetterGlamorPlates.Shared.Ui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace BetterGlamorPlates.Features.Main;

/// <summary>
/// Renders immutable controller state and translates deliberate player input
/// into queued commands. It owns only UI-specific fonts and a bounded texture
/// lookup cache.
/// </summary>
internal sealed partial class MainWindow : Window, IDisposable
{
    internal const int MaximumCachedIcons = 256;

    private readonly MainController controller;
    private readonly Action<MainCommand> issueCommand;
    internal WindowBounds? NativeBounds { get; set; }
    private readonly ITextureProvider textureProvider;
    private readonly OwnedResourcePair<IFontHandle> ownedFonts;
    private readonly IFontHandle sectionFont;
    private readonly IFontHandle titleFont;
    private readonly byte[] collectionFilterBuffer = new byte[96];
    private readonly byte[] plateSearchBuffer = new byte[96];
    private readonly byte[] nameBuffer = new byte[96];
    private readonly BoundedIconCache<ISharedImmediateTexture> icons = new(MaximumCachedIcons);
    internal bool LoadQueued { get; set; }
    internal Action? CancelLoad { get; set; }
    internal IReadOnlyList<GameIconOption> GameIcons { get; set; } = [];
    internal bool GameIconsReady { get; set; }
    internal bool GameIconsUnavailable { get; set; }
    internal Action? OpenDiagnostics { get; set; }
    private IReadOnlyList<DyeOption> dyes = [];
    internal IReadOnlyList<DyeOption> Dyes { get => dyes; set => dyes = DyePaletteOrder.Sort(value); }
    internal bool NativeDyeWindowOpen { get; set; }
    private readonly DrawerSession drawers = new();
    private bool stylesPushed;
    private bool disposed;

    internal MainWindow(
        IDalamudPluginInterface pluginInterface,
        MainController controller,
        ITextureProvider textureProvider,
        Action<MainCommand> issueCommand)
        : base(PluginIdentity.WindowId)
    {
        this.controller = controller;
        this.textureProvider = textureProvider;
        this.issueCommand = issueCommand;

        using (pluginInterface.UiBuilder.FontAtlas.SuppressAutoRebuild())
        {
            ownedFonts = new OwnedResourcePair<IFontHandle>(
                () => pluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.Jupiter, 17f)),
                () => pluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.Jupiter, 22f)));
            sectionFont = ownedFonts.First;
            titleFont = ownedFonts.Second;
        }

        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings;
        ShowCloseButton = false;
        AllowPinning = false;
        AllowClickthrough = false;
        RespectCloseHotkey = false;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        icons.Clear();
        ownedFonts.Dispose();
    }

    public override void OnClose()
    {
        drawers.Close();
        issueCommand(new MainCommand.CloseEditor());
    }

    private void IssueDrawerCommand(MainCommand? command)
    {
        if (command is not null) issueCommand(command);
    }

    public override bool DrawConditions() => !NativeDyeWindowOpen;

    public override void PreDraw()
    {
        drawerReserved = drawers.DyeOpen;
        iconDrawerReserved = drawers.IconOpen;
        var viewport = ImGui.GetMainViewport();
        var placement = NativeBounds is { } native
            ? CompanionPlacementPolicy.Calculate(native, new WindowBounds(viewport.WorkPos.X,
                viewport.WorkPos.Y, viewport.WorkSize.X, viewport.WorkSize.Y), drawers.IconOpen ? IconDrawerWidth + 4 : drawers.DyeOpen ? DyeDrawerWidth + 4 : 0)
            : null;
        if (placement is { } fit)
        {
            designScale = fit.Scale;
            ImGui.SetNextWindowPos(fit.Position, ImGuiCond.Always);
            ImGui.SetNextWindowSize(fit.Size, ImGuiCond.Always);
        }
        if (placement is null)
            IsOpen = false;
        PluginTheme.Push(designScale);
        stylesPushed = true;
    }

    public override void PostDraw()
    {
        if (!stylesPushed)
            return;

        PluginTheme.Pop();
        stylesPushed = false;
    }

    private void DrawCollectionTile(PlateSlot selectedSlot, CollectionItem item, float tileSize)
    {
        ImGui.PushID(item.StableKey);
        var clicked = ImGui.Selectable(
            "##collection-tile",
            controller.View.Slots.Any(slot => slot.Slot == selectedSlot && slot.ItemId == item.ItemId),
            ImGuiSelectableFlags.None,
            new Vector2(tileSize));
        var hovered = ImGui.IsItemHovered();
        var minimum = ImGui.GetItemRectMin();
        if (controller.View.Slots.Any(slot => slot.Slot == selectedSlot && slot.ItemId == item.ItemId))
            OutlineLastItem();
        var after = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        ImGui.SetCursorScreenPos(minimum + new Vector2(S(4), S(4)));
        DrawIcon(item.IconId, Math.Max(1, tileSize - S(8)));
        ImGui.SetCursorScreenPos(after);
        NativePanelChrome.DrawSourceGlyph(
            drawList,
            item.Source == CollectionSource.Dresser
                ? CollectionSourceGlyph.Dresser
                : CollectionSourceGlyph.Armoire,
            minimum + new Vector2(tileSize - S(9), tileSize - S(9)),
            item.Freshness == CollectionFreshness.Fresh ? PluginTheme.Positive : PluginTheme.Warning);
        if (clicked)
            issueCommand(new MainCommand.AddItem(selectedSlot, item));
        if (hovered)
        {
            var source = item.Source == CollectionSource.Dresser ? "Glamour Dresser" : "Armoire";
            ImGui.SetTooltip($"{item.Name}\n{source} · {item.FreshnessLabel}\nTry on and use for {PlateRules.GetSlotName(selectedSlot)} in the local outfit");
        }

        ImGui.PopID();
    }

    private void DrawIcon(uint iconId, float size)
    {
        if (iconId == 0)
        {
            ImGui.Dummy(new Vector2(size, size));
            return;
        }

        var texture = icons.Get(iconId, id => textureProvider.GetFromGameIcon(new GameIconLookup(id)));

        var wrap = texture.GetWrapOrEmpty();
        if (wrap.Handle.Handle == 0)
            ImGui.Dummy(new Vector2(size, size));
        else
            ImGui.Image(wrap.Handle, new Vector2(size, size));
    }

    private void DrawSectionHeading(string text)
    {
        using (sectionFont.Push())
            ImGui.TextColored(PluginTheme.PearlText, text);
    }

    private static string ReadBuffer(byte[] buffer) => UiText.Read(buffer);
    private static void WriteBuffer(byte[] buffer, string value) => UiText.Write(buffer, value);
}
