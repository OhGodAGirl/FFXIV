using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;

namespace EZDelivery.Features.Interface;

/// <summary>ImGui rendering only. Textures are requested during framework preparation, never from native data in Draw.</summary>
public sealed class InterfaceRenderer(InterfaceFacade facade) : IDisposable
{
    private readonly TextureCache<ISharedImmediateTexture> textures = new();
    private bool disposed;
    private static Vector4 Gold => PluginTheme.Gold;
    private static Vector4 Secondary => PluginTheme.TextMuted;
    private static Vector4 Warning => PluginTheme.Gold;
    private static readonly string[] TabStyleNames = Enum.GetValues<ShopTabStyle>().Select(TabPresentation.Name).ToArray();

    /// <summary>Prepare shared resource references on the framework thread. Do not retain frame-only texture wraps.</summary>
    public void PrepareTextures(ITextureProvider provider, IEnumerable<ShopRow> rows)
    {
        if (disposed) return;
        textures.Prepare(rows.Select(row => row.IconId), id =>
            provider.TryGetFromGameIcon(new GameIconLookup(id), out var texture) ? texture : null);
    }

    /// <summary>Draws prepared UI state and enqueues user intents without native transactions.</summary>
    public void Draw()
    {
        if (disposed) return;
        var state = facade.State;
        var scale = ContextLayout.SafeScale(ImGuiHelpers.GlobalScale);
        var viewport = ImGui.GetMainViewport();
        state = state with
        {
            DeliveryAnchor = ContextLayout.ToViewport(state.DeliveryAnchor, viewport.Pos.X, viewport.Pos.Y),
            ShopAnchor = ContextLayout.ToViewport(state.ShopAnchor, viewport.Pos.X, viewport.Pos.Y),
        };
        var bounds = new UiRect(viewport.WorkPos.X, viewport.WorkPos.Y, viewport.WorkSize.X, viewport.WorkSize.Y);
        string? placementReason = null;
        PushStyle(scale);
        try
        {
            if (state.LoggedIn)
            {
                if (state.ExpertDeliverySelected && ContextLayout.Delivery(state.DeliveryAnchor, bounds, scale) is { } delivery)
                {
                    delivery = ContextLayout.BridgeToFrame(delivery, bounds, state.DeliveryAnchor!.Bounds, scale);
                    DrawDelivery(state, delivery, scale);
                }
                else if (state.ExpertDeliverySelected && state.DeliveryAnchor is { Ready: true })
                    placementReason = "Move the Expert Delivery window right to make room for its bookmark.";
                var shop = ContextLayout.Shop(state.ShopAnchor, bounds, scale, facade.SelectedCategory is not null);
                if (state.ShopAnchor is { Ready: true } && shop.Bookmarks.Count == 0)
                    placementReason = "Move the Seal Exchange window right to make room for its category bookmarks.";
                else if (shop.PlacementReason is not null) placementReason = shop.PlacementReason;
                for (var i = 0; i < shop.Bookmarks.Count; i++)
                {
                    var tab = shop.Bookmarks[i];
                    // Bridge the layout's safety gap toward the frame, never into native controls or outside the viewport.
                    tab = ContextLayout.BridgeToFrame(tab, bounds, state.ShopAnchor!.Bounds, scale);
                    DrawShopBookmark(state, (ShopCategory)i, tab, shop.PlacementReason, scale);
                }
                if (shop.Browser is { } browser) DrawBrowser(state, browser, scale);
            }
            DrawSettings(state, scale, placementReason);
            DrawDiagnostics(state, scale);
        }
        finally { ImGui.PopStyleVar(5); PluginTheme.Pop(); }
    }

    private void DrawDelivery(UiState state, UiRect rect, float scale)
    {
        var canUse = state.Delivery.Running || (state.Delivery.CanStart && !state.AnyOperationRunning);
        if (Bookmark("Expert Deliveries", rect, state.Delivery.Running, canUse, null, scale, state.ShopTabStyle))
            facade.Enqueue(state.Delivery.Running ? new StopDeliveryIntent(state.Generation) : new StartDeliveryIntent(state.Generation));
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) || ImGui.IsItemFocused())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(300 * scale);
            ImGui.TextUnformatted("EZ Delivery");
            ImGui.TextColored(Secondary, "Fast(er) Expert Deliveries");
            ImGui.Separator();
            ImGui.TextUnformatted(state.Delivery.Running ? "Stop Expert Deliveries" : "Start Expert Deliveries");
            ImGui.TextWrapped(state.Delivery.Running ? UiCopy.Stop : "Stops before the next reward exceeds your seal capacity.");
            if (state.Delivery.Status.Length > 0) ImGui.TextWrapped(state.Delivery.Status);
            if (state.Delivery.Running || state.Delivery.ConfirmedCount > 0)
                ImGui.TextUnformatted($"Confirmed: {state.Delivery.ConfirmedCount:N0}");
            if (!canUse && state.AnyOperationRunning) ImGui.TextWrapped("Stop the current purchase operation first.");
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
        ImGui.End();
    }

    private void DrawShopBookmark(UiState state, ShopCategory category, UiRect rect, string? placementReason, float scale)
    {
        if (Bookmark(category.ToString(), rect, facade.SelectedCategory == category, true, category, scale, state.ShopTabStyle))
            facade.Enqueue(new SelectCategoryIntent(state.Generation, category));
        Tooltip(placementReason is null ? category.ToString() : $"{category}\n{placementReason}", scale);
        ImGui.End();
    }

    // Begins an attached window and leaves it open so callers can attach tooltip to the button.
    private static bool Bookmark(string id, UiRect rect, bool selected, bool enabled, ShopCategory? category, float scale, ShopTabStyle tabStyle = ShopTabStyle.InsetSockets)
    {
        ImGui.SetNextWindowPos(new(rect.X, rect.Y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new(rect.Width, rect.Height), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.Border, Vector4.Zero);
        ImGui.Begin($"##EZDelivery.Bookmark.{id}", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar();
        ImGui.BeginDisabled(!enabled);
        // A regular button preserves keyboard/gamepad focus and activation semantics.
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Vector4.Zero);
        bool clicked;
        try { clicked = ImGui.Button($"##{id}", new(rect.Width, rect.Height)); }
        finally { ImGui.PopStyleColor(3); ImGui.EndDisabled(); }
        var draw = ImGui.GetWindowDrawList();
        var min = new Vector2(rect.X + 1, rect.Y + 1);
        var max = new Vector2(rect.Right - 1, rect.Bottom - 1);
        var center = (min.Y + max.Y) / 2;
        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) || ImGui.IsItemFocused();
        var palette = ThemePalette.Resolve(PluginTheme.CurrentTheme);
        var fill = selected ? palette.Active : hovered ? palette.Hover : palette.Surface;
        var color = ImGui.GetColorU32(fill);
        var border = ImGui.GetColorU32(selected || hovered ? palette.Icon : palette.Border);
        DrawShopTab(draw, min, max, color, border, scale, tabStyle);
        var glyphScale = scale * (4f / 3f);
        var glyphWidth = category is null ? 26 : 24;
        var iconMin = new Vector2(rect.X + (rect.Width - glyphWidth * glyphScale) / 2, rect.Y + (rect.Height - 24 * glyphScale) / 2);
        var gold = ImGui.GetColorU32(enabled ? palette.Icon : Secondary);
        if (category is null) BookmarkGlyphs.ParcelAndArrow(draw, iconMin, glyphScale, gold);
        else BookmarkGlyphs.Category(draw, category.Value, iconMin, glyphScale, gold, color);
        return clicked;
    }

    private static void DrawShopTab(ImDrawListPtr draw, Vector2 min, Vector2 max, uint fill, uint border, float scale, ShopTabStyle style)
    {
        var canvas = new ImGuiTabCanvas(draw);
        TabPresentation.Draw(ref canvas, min, max, scale, style, fill, border,
            ImGui.GetColorU32(ThemePalette.Resolve(PluginTheme.CurrentTheme).Inset));
    }

    private readonly struct ImGuiTabCanvas(ImDrawListPtr draw) : ITabCanvas
    {
        public void FillRect(Vector2 min, Vector2 max, uint color, float rounding = 0, bool leftCornersOnly = false)
            => draw.AddRectFilled(min, max, color, rounding, leftCornersOnly ? ImDrawFlags.RoundCornersLeft : ImDrawFlags.None);
        public void OutlineRect(Vector2 min, Vector2 max, uint color, float rounding, bool leftCornersOnly = false)
            => draw.AddRect(min, max, color, rounding, leftCornersOnly ? ImDrawFlags.RoundCornersLeft : ImDrawFlags.None);
        public void FillCircle(Vector2 center, float radius, uint color, int segments) => draw.AddCircleFilled(center, radius, color, segments);
        public void FillTriangle(Vector2 first, Vector2 second, Vector2 third, uint color) => draw.AddTriangleFilled(first, second, third, color);
        public void Line(Vector2 first, Vector2 second, uint color, float thickness) => draw.AddLine(first, second, color, thickness);
        public void PushClip(Vector2 min, Vector2 max) => draw.PushClipRect(min, max, false);
        public void PathPoint(Vector2 point) => draw.PathLineTo(point);
        public void PathArc(Vector2 center, float radius, float start, float end, int segments) => draw.PathArcTo(center, radius, start, end, segments);
        public void StrokeOpenPath(uint color, float thickness) => draw.PathStroke(color, ImDrawFlags.None, thickness);
        public void PopClip() => draw.PopClipRect();
    }

    private void DrawBrowser(UiState state, UiRect rect, float scale)
    {
        ImGui.SetNextWindowPos(new(rect.X, rect.Y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new(rect.Width, rect.Height), ImGuiCond.Always);
        var open = true;
        var showing = ImGui.Begin("EZ Delivery##EZDelivery.Browser", ref open, ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);
        try
        {
            if (!open) facade.Enqueue(new CloseBrowserIntent(state.Generation));
            if (!showing) return;
            var category = facade.SelectedCategory!.Value;
            var view = facade.Browser;
            var compact = ImGui.GetContentRegionAvail().X < 480 * scale;
            var footerHeight = (compact ? 100 : 82) * scale;
            var headerWanted = (compact ? 119 : 91) * scale +
                (category == ShopCategory.Furniture ? 38 * scale : 0) +
                (!state.Shopping.CollectionUsable ? 38 * scale : 0);
            var headerHeight = Math.Clamp(headerWanted, 28 * scale,
                Math.Max(28 * scale, ImGui.GetContentRegionAvail().Y - footerHeight - 36 * scale));
            ImGui.BeginChild("##Browser header", new(0, headerHeight), false);
            ImGui.TextColored(Gold, category.ToString().ToUpperInvariant());
            if (state.Shopping.CollectionUsable)
            {
                ImGui.SameLine();
                ImGui.TextColored(Secondary, $"{view.MissingCount:N0} Missing");
            }
            var query = facade.Search;
            if (!compact) { ImGui.SameLine(); ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowWidth() - 206 * scale)); }
            ImGui.SetNextItemWidth(compact ? -1 : 194 * scale);
            if (ImGui.InputTextWithHint("##Filter items", "Filter items…", ref query, 160))
                facade.Enqueue(new SearchIntent(state.Generation, query));
            ImGui.TextColored(Secondary, "Fast(er) Expert Deliveries");
            ImGui.TextWrapped(state.Shopping.CollectionStatus);
            if (!state.Shopping.CollectionUsable)
                ImGui.TextWrapped("Open Exportaramus and refresh the required sources.");
            if (category == ShopCategory.Furniture)
                ImGui.TextWrapped("Storage presence only; placed furnishings are not included.");
            ImGui.EndChild();
            ImGui.Separator();
            var headerStart = ImGui.GetCursorScreenPos();
            var rowWidth = ImGui.GetContentRegionAvail().X;
            ImGui.TextColored(Secondary, "Item");
            if (!compact)
            {
                ImGui.SameLine();
                ImGui.SetCursorScreenPos(new(headerStart.X + rowWidth - 154 * scale, headerStart.Y));
                ImGui.TextColored(Secondary, "Seals");
            }
            ImGui.SameLine();
            ImGui.SetCursorScreenPos(new(headerStart.X + rowWidth - 62 * scale, headerStart.Y));
            ImGui.TextColored(Secondary, "Buy");
            var listHeight = Math.Max(28 * scale, ImGui.GetContentRegionAvail().Y - footerHeight);
            if (ImGui.BeginChild("##Item region", new(0, listHeight), false))
            {
                if (!state.Shopping.CollectionUsable) ImGui.TextWrapped("Missing items cannot be determined yet.");
                else if (view.Rows.Count == 0) ImGui.TextWrapped(view.SearchActive ? "No missing items match this filter." : "No missing items in this category.");
                else DrawRows(state, view.Rows, compact, scale);
            }
            ImGui.EndChild();
            DrawFooter(state, view, compact, scale);
        }
        finally { ImGui.End(); }
    }

    private void DrawRows(UiState state, IReadOnlyList<ShopRow> rows, bool compact, float scale)
    {
        var lineHeight = ImGui.GetTextLineHeight();
        var rowHeight = Math.Max(40 * scale, lineHeight * (compact ? 3 : 2) + 8 * scale);
        var scroll = ImGui.GetScrollY();
        var first = Math.Max(0, (int)(scroll / rowHeight) - 1);
        var last = Math.Min(rows.Count, first + (int)(ImGui.GetWindowHeight() / rowHeight) + 3);
        var initial = ImGui.GetCursorPosY();
        if (first > 0) ImGui.SetCursorPosY(initial + first * rowHeight);
        for (var i = first; i < last; i++)
        {
            var row = rows[i];
            ImGui.PushID(row.OfferKey);
            var start = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            if (state.Shopping.Running && state.Shopping.CurrentOfferKey == row.OfferKey)
                ImGui.GetWindowDrawList().AddRectFilled(start, start + new Vector2(width, rowHeight), ImGui.GetColorU32(PluginTheme.Themed(new Vector4(.34f, .32f, .25f, 1))));
            else if (i % 2 == 1) ImGui.GetWindowDrawList().AddRectFilled(start, start + new Vector2(width, rowHeight), ImGui.GetColorU32(PluginTheme.Themed(new Vector4(.255f, .263f, .267f, 1))));
            ImGui.SetCursorScreenPos(start + new Vector2(4 * scale, 4 * scale));
            DrawIcon(row, 32 * scale);
            if (ImGui.IsItemHovered()) DrawItemTooltip(row, scale);
            var nameX = start.X + 42 * scale;
            var buttonX = start.X + width - 66 * scale;
            var nameEnd = compact ? buttonX - 6 * scale : buttonX - 94 * scale;
            ImGui.SetCursorScreenPos(new(nameX, start.Y + 4 * scale));
            ImGui.PushClipRect(new(nameX, start.Y), new(nameEnd, start.Y + 2 * lineHeight + 4 * scale), true);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(1, nameEnd - nameX));
            ImGui.TextWrapped(row.Name);
            ImGui.PopTextWrapPos();
            ImGui.PopClipRect();
            if (ImGui.IsItemHovered()) DrawItemTooltip(row, scale);
            ImGui.SetCursorScreenPos(compact ? new(nameX, start.Y + 2 * lineHeight + 4 * scale) : new(buttonX - 88 * scale, start.Y + 10 * scale));
            ImGui.TextColored(Secondary, compact ? $"{row.SealCost:N0} seals" : $"{row.SealCost:N0}");
            ImGui.SetCursorScreenPos(new(buttonX, start.Y + 5 * scale));
            var canBuy = row.CanBuy && row.SealCost <= state.Shopping.SealBalance && !state.AnyOperationRunning;
            ImGui.BeginDisabled(!canBuy);
            if (ImGui.Button("Buy", new(62 * scale, 28 * scale))) facade.Enqueue(new BuyOneIntent(state.Generation, row.OfferKey));
            ImGui.EndDisabled();
            var reason = state.AnyOperationRunning ? "An operation is already running." : !row.CanBuy ? row.DisabledReason : row.SealCost > state.Shopping.SealBalance ? "Not enough seals." : UiCopy.Buy(row);
            Tooltip(reason, scale);
            ImGui.PopID();
            ImGui.SetCursorPosY(initial + (i + 1) * rowHeight);
        }
        ImGui.SetCursorPosY(initial + rows.Count * rowHeight);
        ImGui.Dummy(new(1, 1));
    }

    private void DrawFooter(UiState state, BrowserView view, bool compact, float scale)
    {
        ImGui.Separator();
        var areaStart = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var actionY = ImGui.GetWindowPos().Y + ImGui.GetWindowSize().Y - 34 * scale;
        var summaryY = compact ? actionY - ImGui.GetTextLineHeightWithSpacing() : actionY + 4 * scale;
        var statusHeight = Math.Max(1, summaryY - areaStart.Y - 4 * scale);
        ImGui.BeginChild("##Operation status", new(0, statusHeight), false);
        if (state.Shopping.Running)
            ImGui.TextUnformatted($"Purchasing · {state.Shopping.ConfirmedCount:N0} confirmed");
        else if (view.TotalCost is { } total && total > state.Shopping.SealBalance)
            ImGui.TextColored(Warning, "Not enough seals for all items");
        else if (view.TotalCost is null) ImGui.TextColored(Warning, "The total seal cost could not be verified.");
        if (state.Shopping.Status.Length > 0) ImGui.TextWrapped(state.Shopping.Status);
        ImGui.EndChild();
        ImGui.SetCursorScreenPos(new(areaStart.X, summaryY));
        ImGui.PushClipRect(new(areaStart.X, summaryY), new(areaStart.X + width - (compact ? 0 : 108 * scale), actionY + 28 * scale), true);
        if (state.Shopping.CollectionUsable)
            ImGui.TextUnformatted(view.TotalCost is { } cost ? $"{view.BuyAllKeys.Count:N0} items · Total: {cost:N0} seals" : $"{view.BuyAllKeys.Count:N0} items · Total unavailable");
        else ImGui.TextUnformatted("Missing count unavailable");
        ImGui.PopClipRect();
        ImGui.SetCursorScreenPos(new(compact ? areaStart.X : areaStart.X + width - 100 * scale, actionY));
        var enabled = state.Shopping.Running || (!state.AnyOperationRunning && state.Shopping.CollectionUsable && view.BuyAllKeys.Count > 0 && view.TotalCost is not null);
        ImGui.BeginDisabled(!enabled);
        if (ImGui.Button(state.Shopping.Running ? "Stop" : "Buy All", new(compact ? -1 : 100 * scale, 28 * scale)))
            facade.Enqueue(state.Shopping.Running ? new StopPurchasesIntent(state.Generation) : new BuyAllIntent(state.Generation, view.BuyAllKeys));
        ImGui.EndDisabled();
        Tooltip(state.Shopping.Running ? UiCopy.Stop : !state.Shopping.CollectionUsable ? "Collection data unavailable. Open Exportaramus and refresh the required sources." : state.AnyOperationRunning ? "Stop the current Expert Delivery operation first." : UiCopy.BuyAll, scale);
    }

    private void DrawIcon(ShopRow row, float size)
    {
        // Shared wraps are valid for this Draw only and must never be retained or disposed by this plugin.
        if (textures.TryGetValue(row.IconId, out var texture) && texture.GetWrapOrDefault() is { } wrap)
            ImGui.Image(wrap.Handle, new(size, size));
        else
        {
            var pos = ImGui.GetCursorScreenPos();
            var draw = ImGui.GetWindowDrawList();
            draw.AddRectFilled(pos, pos + new Vector2(size), ImGui.GetColorU32(PluginTheme.Themed(new Vector4(.20f, .21f, .21f, 1))), 2);
            draw.AddRect(pos, pos + new Vector2(size), ImGui.GetColorU32(Secondary), 2);
            ImGui.Dummy(new(size, size));
        }
    }

    private void DrawItemTooltip(ShopRow row, float scale)
    {
        ImGui.BeginTooltip();
        DrawIcon(row, 96 * scale);
        ImGui.PushTextWrapPos(330 * scale);
        ImGui.TextWrapped(row.Name);
        ImGui.TextUnformatted($"{row.SealCost:N0} seals");
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    private void DrawSettings(UiState state, float scale, string? placementReason)
    {
        if (!facade.SettingsOpen) return;
        ImGui.SetNextWindowSize(new(430 * scale, 0), ImGuiCond.FirstUseEver);
        var open = facade.SettingsOpen;
        if (ImGui.Begin("EZ Delivery · Settings", ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.PushTextWrapPos(415 * scale);
            ImGui.TextWrapped("Open the Personnel Officer's Expert Delivery tab, then click its Parcel & Arrow bookmark to start or stop hand-ins. Your selected native filter applies.");
            ImGui.Spacing();
            ImGui.TextWrapped("Open the Quartermaster's Seal Exchange and choose a category on the left. Buy one item or use Buy All for the missing items shown.");
            if (placementReason is not null)
            {
                ImGui.Spacing();
                ImGui.TextColored(Warning, "More screen space is needed");
                ImGui.TextWrapped(placementReason);
            }
            ImGui.Spacing();
            var tabStyle = (int)state.ShopTabStyle;
            if (ImGui.BeginCombo("Tab style", TabStyleNames[tabStyle]))
            {
                for (var i = 0; i < TabStyleNames.Length; i++)
                    if (ImGui.Selectable(TabStyleNames[i], i == tabStyle))
                        facade.Enqueue(new ShopTabStyleIntent(state.Generation, (ShopTabStyle)i));
                ImGui.EndCombo();
            }
            ImGui.TextColored(Secondary, "Applies immediately to Expert Delivery and Quartermaster tabs.");
            var reduce = state.ReduceMotion;
            if (ImGui.Checkbox("Reduce Motion", ref reduce)) facade.Enqueue(new ReduceMotionIntent(state.Generation, reduce));
            ImGui.TextColored(Secondary, "Context windows follow the game without animation.");
            if (ImGui.Button("Diagnostics", new(120 * scale, 28 * scale))) facade.DiagnosticsOpen = true;
            ImGui.PopTextWrapPos();
        }
        ImGui.End();
        facade.SettingsOpen = open;
    }

    private void DrawDiagnostics(UiState state, float scale)
    {
        if (!facade.DiagnosticsOpen) return;
        ImGui.SetNextWindowSize(new(650 * scale, 440 * scale), ImGuiCond.FirstUseEver);
        var open = facade.DiagnosticsOpen;
        if (ImGui.Begin("EZ Delivery · Diagnostics", ref open))
        {
            if (ImGui.Button("Copy prepared report", new(180 * scale, 28 * scale)))
                facade.Enqueue(new CopyDiagnosticsIntent(state.Generation, state.DiagnosticsReport));
            ImGui.Separator();
            if (ImGui.BeginChild("##Prepared report", Vector2.Zero, false))
                ImGui.TextWrapped(state.DiagnosticsReport);
            ImGui.EndChild();
        }
        ImGui.End();
        facade.DiagnosticsOpen = open;
    }

    private static void Tooltip(string text, float scale)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && !ImGui.IsItemFocused()) return;
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(320 * scale);
        ImGui.TextWrapped(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    private static void PushStyle(float scale)
    {
        PluginTheme.Push();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6 * scale));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6 * scale, 4 * scale));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 3 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, scale);
    }

    /// <summary>Clears renderer texture references and prepared icon state.</summary>
    public void Dispose()
    {
        disposed = true;
        textures.Dispose();
    }
}
