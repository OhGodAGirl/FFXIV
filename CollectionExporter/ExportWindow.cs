using System;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace CollectionExporter;

public sealed class ExportWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private ExportResult? result;
    private byte[] previewBuffer = Array.Empty<byte>();
    private readonly byte[] profileBuffer = new byte[256];
    private string status = "Press Generate to read your collections.";
    private bool generating;

    public ExportWindow(Plugin plugin)
        : base("CollectionExporter##CollectionExporter")
    {
        this.plugin = plugin;
        SetProfileBuffer(plugin.ConfiguredProfile);
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 430),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        ImGui.TextWrapped("Create import data for FFXIV Collect from your character's collections.");

        ImGui.Spacing();
        ImGui.Text("Optional FFXIV Collect profile (Outfits and Relics):");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##profile", profileBuffer.AsSpan());
        if (ImGui.Button("Save profile"))
        {
            var input = ReadProfileBuffer();
            plugin.TrySetProfile(input, out status);
        }

        ImGui.Spacing();

        if (!generating && ImGui.Button("Generate"))
            Generate();

        ImGui.SameLine();

        if (result is not null)
        {
            if (ImGui.Button("Copy JSON"))
            {
                ImGui.SetClipboardText(result.Json);
                status = "Copied JSON to clipboard.";
            }
        }

        ImGui.Spacing();
        ImGui.TextWrapped(status);

        if (result is null)
            return;

        ImGui.Separator();
        ImGui.Text("Included collections:");

        foreach (var pair in result.Collections)
            ImGui.BulletText($"{pair.Key}: {pair.Value.Length}");

        if (result.OmittedCollections.Count > 0)
        {
            ImGui.Spacing();
            ImGui.Text("Not included:");
            foreach (var pair in result.OmittedCollections)
                ImGui.BulletText($"{pair.Key}: {pair.Value}");
        }

        ImGui.Spacing();
        ImGui.TextWrapped(
            "Important: FFXIV Collect's importer replaces each collection key that is present. " +
            "Review the counts on its verification screen before submitting.");

        ImGui.Spacing();

        ImGui.InputTextMultiline(
            "##json",
            previewBuffer.AsSpan(),
            new Vector2(-1, 210),
            ImGuiInputTextFlags.ReadOnly);
    }

    private async void Generate()
    {
        generating = true;
        status = "Reading your collections...";
        try
        {
            await plugin.RefreshOnlineCollectionsAsync();
            result = plugin.BuildExport();
            previewBuffer = Encoding.UTF8.GetBytes(result.Json + '\0');
            status = $"Created import data for {result.Collections.Count} collections.";
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to build FFXIV Collect export.");
            result = null;
            previewBuffer = Array.Empty<byte>();
            status = ex.Message;
        }
        finally
        {
            generating = false;
        }
    }

    private string ReadProfileBuffer()
    {
        var nullIndex = Array.IndexOf(profileBuffer, (byte)0);
        var length = nullIndex >= 0 ? nullIndex : profileBuffer.Length;
        return Encoding.UTF8.GetString(profileBuffer, 0, length);
    }

    private void SetProfileBuffer(string value)
    {
        Array.Clear(profileBuffer);
        var bytes = Encoding.UTF8.GetBytes(value);
        Array.Copy(bytes, profileBuffer, Math.Min(bytes.Length, profileBuffer.Length - 1));
    }
}
