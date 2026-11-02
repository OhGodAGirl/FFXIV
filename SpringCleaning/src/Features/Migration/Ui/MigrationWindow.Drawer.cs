using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using SpringCleaning.Shared.CollectionData;

namespace SpringCleaning;

internal sealed partial class MigrationWindow
{
    private bool showIgnoredDuplicates;
    private readonly DuplicateReviewCache duplicateReviewCache = new();

    internal bool IsCleanupRunning => cleanup.IsRunning;

    private void DrawDrawerDuplicates()
    {
        var (allEntries, review) = duplicateReviewCache.Get(
            inventoryDuplicates, controller.Alerts, configuration.IgnoredDuplicateItemIds);
        bool IgnoredEntry(DresserAlertEntry entry) =>
            DuplicateReviewCache.IsIgnored(entry, configuration.IgnoredDuplicateItemIds);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted($"I've found {review.Count:N0} duplicates, kupo!");
        var ignoredWidth = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X
            + ImGui.CalcTextSize("Show Ignored").X;
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - ignoredWidth);
        ImGui.Checkbox("Show Ignored", ref showIgnoredDuplicates);
        var displayed = showIgnoredDuplicates ? allEntries : review;
        var status = cleanup.Status ?? duplicateDiscardController.Status;
        if (!string.IsNullOrWhiteSpace(status))
            ImGui.TextWrapped(status);
        var ready = controller.HasFreshDuplicateOwnership;
        var presentationReady = ready || RetainPage;
        var footer = 32f + ImGui.GetStyle().ItemSpacing.Y;
        ImGui.BeginChild("##DrawerDuplicates", new Vector2(0f,
            MathF.Max(1f, ImGui.GetContentRegionAvail().Y - footer)), false);
        if (!ready && !RetainPage)
            ImGui.TextWrapped(controller.OwnershipReadiness(CollectionSourceName.Inventory,
                CollectionSourceName.GlamourDresser, CollectionSourceName.Armoire));
        else
        {
            var rowIndex = 0;
            foreach (var copy in displayed.InventoryDuplicates)
            {
                DrawDrawerRow($"copy-{copy.InventoryTypeId}-{copy.SlotIndex}", copy.Name,
                    copy.IconId, copy.IsHighQuality,
                    copy.Source == CollectionSourceName.ArmoryChest ? "Armoury Chest" : "Inventory",
                    presentationReady && !cleanup.IsRunning && !configuration.IgnoredDuplicateItemIds.Contains(copy.ItemId) && InventoryDuplicatePlanner.CanBulkDiscard(copy),
                    copy.Safety.IsSafe ? $"Discard {copy.Name}" : copy.Safety.Status,
                    () => cleanup.Start([copy], []), [copy.ItemId], rowIndex++);
            }
            foreach (var entry in displayed.DresserEntries)
            {
                DrawDrawerRow($"dresser-{entry.PrismBoxIndex}-{entry.RawItemId}", entry.Name,
                    entry.IconId, entry.IsHighQuality, "Glamour Dresser",
                    presentationReady && !cleanup.IsRunning && !IgnoredEntry(entry) && IsStoredEligible(entry),
                    $"Restore and discard {entry.Name}",
                    () => cleanup.Start([], [entry]), DresserRestorationPolicy.Components(entry).Append(entry.ItemId).Distinct().ToArray(), rowIndex++);
            }
        }
        ImGui.EndChild();
        ImGui.SetCursorPosY(MathF.Max(ImGui.GetCursorPosY(), ImGui.GetWindowContentRegionMax().Y - 32f));
        if (cleanup.IsRestoring)
        {
            var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X - 42f);
            ImGui.ProgressBar(cleanup.RestoreTotal == 0 ? 0f : (float)cleanup.RestoreCompleted / cleanup.RestoreTotal,
                new Vector2(width, 32f), $"{cleanup.RestoreCompleted} / {cleanup.RestoreTotal}");
            ImGui.SameLine();
            using (pluginInterface.UiBuilder.IconFontHandle.Push())
                if (SpringCleaningUi.DrawNativeButton("CancelRestoration", FontAwesomeIcon.Times.ToIconString(), new Vector2(32f), false))
                    cleanup.Cancel();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Cancel restoring duplicates.");
        }
        else if (duplicateDiscardController.IsBulkRunning)
            DrawBulkDiscardControl(review.InventoryDuplicates);
        else
        {
            var eligible = review.InventoryDuplicates.Any(InventoryDuplicatePlanner.CanBulkDiscard)
                || review.DresserEntries.Any(IsStoredEligible);
            if (SpringCleaningUi.DrawNativeButton("DrawerDiscardAll", "Discard All Duplicates",
                    new Vector2(MathF.Max(1f, ImGui.GetContentRegionAvail().X), 32f),
                    !presentationReady || !eligible || cleanup.IsRunning) && ready && !cleanup.IsRunning)
                cleanup.Start(review.InventoryDuplicates, review.DresserEntries);
        }
    }

    private void DrawDrawerRow(string id, string name, uint icon, bool highQuality, string location,
        bool visuallyEnabled, string tooltip, Action action, uint[] itemIds, int rowIndex)
    {
        var itemLabel = name + (highQuality ? " HQ" : string.Empty);
        var locationLabel = itemIds.Any(configuration.IgnoredDuplicateItemIds.Contains)
            ? $"Ignored - {location}" : $"Duplicate found in {location}";
        var iconSize = ImGui.GetTextLineHeight() * 2f + ImGui.GetStyle().ItemSpacing.Y;
        // Include wrapped text when sizing the square preview to the row contents.
        for (var pass = 0; pass < 4; pass++)
        {
            var textWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X - iconSize - 30f
                - ImGui.GetStyle().CellPadding.X * 6f);
            iconSize = MathF.Max(iconSize, ImGui.CalcTextSize(itemLabel, false, textWidth).Y
                + ImGui.CalcTextSize(locationLabel, false, textWidth).Y + ImGui.GetStyle().ItemSpacing.Y);
        }
        ImGui.PushID(id);
        if (ImGui.BeginTable("##row", 3, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("##icon", ImGuiTableColumnFlags.WidthFixed, iconSize);
            ImGui.TableSetupColumn("##item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("##action", ImGuiTableColumnFlags.WidthFixed, 30f);
            ImGui.TableNextRow();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0,
                ImGui.GetColorU32(rowIndex % 2 == 0 ? ImGuiCol.TableRowBg : ImGuiCol.TableRowBgAlt));
            ImGui.TableSetColumnIndex(0);
            DrawGameIcon(icon, highQuality, iconSize);
            DrawIgnoreContext("icon", itemIds);
            ImGui.TableSetColumnIndex(1);
            ImGui.TextWrapped(itemLabel);
            DrawIgnoreContext("name", itemIds);
            ImGui.PushStyleColor(ImGuiCol.Text, SpringCleaningUi.TextMuted);
            ImGui.TextWrapped(locationLabel);
            DrawIgnoreContext("location", itemIds);
            ImGui.PopStyleColor();
            ImGui.TableSetColumnIndex(2);
            using (pluginInterface.UiBuilder.IconFontHandle.Push())
                if (SpringCleaningUi.DrawNativeButton("Discard", FontAwesomeIcon.Trash.ToIconString(),
                    new Vector2(28f), !visuallyEnabled) && visuallyEnabled
                    && controller.HasFreshDuplicateOwnership && !cleanup.IsRunning)
                    action();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(tooltip);
            ImGui.EndTable();
        }
        ImGui.PopID();
    }

    private void DrawIgnoreContext(string id, uint[] itemIds)
    {
        if (!ImGui.BeginPopupContextItem(id))
            return;
        var ignored = itemIds.Any(configuration.IgnoredDuplicateItemIds.Contains);
        if (ImGui.MenuItem(ignored ? "Stop Ignoring Duplicate" : "Ignore Duplicate", string.Empty,
                false, !cleanup.IsRunning && !duplicateDiscardController.IsAnyRunning))
        {
            foreach (var itemId in itemIds)
                if (ignored) configuration.IgnoredDuplicateItemIds.Remove(itemId);
                else configuration.IgnoredDuplicateItemIds.Add(itemId);
            configurationChanged();
            requestPreviewRefresh(TimeSpan.Zero);
        }
        ImGui.EndPopup();
    }

    private bool IsStoredEligible(DresserAlertEntry entry) =>
        restorableDuplicates.Contains((entry.PrismBoxIndex, entry.RawItemId));
}
