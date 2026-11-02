using System.Globalization;
using Dalamud.Bindings.ImGui;

namespace SpringCleaning;

internal static class DiagnosticStatePanel
{
    internal static void Draw(MigrationDiagnosticSnapshot snapshot, MigrationController controller,
        DresserScanner scanner, NativeUi nativeUi)
    {
        var dresserOpen = snapshot.DresserOpen;
        ImGui.TextColored(SpringCleaningUi.Gold, "CURRENT STATE");
        ImGui.Separator();
        DrawValue("Build", snapshot.Build);
        DrawValue("Station", dresserOpen ? "Glamour Dresser" : "Armoire");
        DrawValue("Mode", snapshot.Mode.ToString());
        DrawValue("Phase", snapshot.Phase.ToString());
        DrawValue("Status", snapshot.Status);
        DrawValue("Current item", snapshot.CurrentItem ?? "None");
        DrawValue("Items stored", snapshot.StoredCount.ToString("N0", CultureInfo.CurrentCulture));
        DrawValue("Queues", $"{snapshot.PendingOutfits:N0} outfits / {snapshot.PendingItems:N0} items");
        DrawValue("Attempts", $"{snapshot.InteractionAttempts:N0} interaction / {snapshot.DresserOpenAttempts:N0} dresser-open");
        if (dresserOpen)
        {
            DrawValue("Dresser data", snapshot.DresserLoaded ? "Ready" : "Not loaded");
            DrawValue("Eligible-item list", snapshot.CandidateListReady ? "Ready" : "Refreshing");
            DrawValue("Glamour Creation", snapshot.GlamourCreationOpen ? "Open" : "Closed");
            DrawValue("Outfit dialog", snapshot.OutfitCreationOpen ? "Open" : "Closed");
            DrawValue("Confirmation", snapshot.ItemConfirmationOpen ? "Open" : "Closed");
            DrawValue(
                "Prisms",
                snapshot.PrismCountReady
                    ? $"{snapshot.PrismsHeld:N0} available / {snapshot.PrismsRequired:N0} planned"
                    : $"Balance unavailable / {snapshot.PrismsRequired:N0} planned; FFXIV verifies at storage");
            DrawValue("Dresser → Armoire", $"{snapshot.EligibleCount:N0} eligible");
            DrawValue(
                "Dresser notices",
                controller.Alerts.Count.ToString("N0", CultureInfo.CurrentCulture));
        }
        else
        {
            DrawValue("Armoire data", scanner.IsCabinetLoaded ? "Ready" : "Not loaded");
            DrawValue("Armoire window", nativeUi.IsArmoireStoreOpen ? "Open" : "Closed");
            DrawValue("Controller active", controller.IsActive ? "Yes" : "No");
        }


    }

    internal static void DrawValue(string label, string value)
    {
        ImGui.TextDisabled(label + ":");
        ImGui.SameLine();
        ImGui.TextWrapped(value);
    }

    internal static void DrawReportLine(string line)
    {
        var separator = line.IndexOf(':');
        if (separator <= 0 || separator + 2 > line.Length)
        {
            ImGui.TextWrapped(line);
            return;
        }

        DrawValue(line[..separator], line[(separator + 2)..]);
    }

}
