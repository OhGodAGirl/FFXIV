using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CollectionShopper;

/// <summary>Clips completed, plugin-owned draw lists without changing their vertex/index data.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows7.0")]
internal sealed class NativeWindowDrawClipper
{
    // Scrollable tables create child windows that do not inherit NoMouseInputs.
    // Update retained children before hit-testing, and newly begun children before
    // submitting their controls. Only this companion's window tree is touched.
    internal static void SetMouseInputBlocked(ImGuiWindowPtr window, bool blocked)
    {
        if (window.IsNull)
            return;
        window.Flags = blocked
            ? window.Flags | ImGuiWindowFlags.NoMouseInputs
            : window.Flags & ~ImGuiWindowFlags.NoMouseInputs;
    }

    internal static void SetTreeMouseInputBlocked(ImGuiWindowPtr window, bool blocked, int remainingDepth = 8)
    {
        if (window.IsNull || remainingDepth == 0)
            return;
        SetMouseInputBlocked(window, blocked);
        var children = window.DC.ChildWindows;
        for (var index = 0; index < children.Size; index++)
            SetTreeMouseInputBlocked(children[index], blocked, remainingDepth - 1);
    }

    private readonly List<ImDrawListPtr> drawLists = [];
    private readonly List<ImDrawCmd> clippedCommands = [];
    private readonly List<MarketBoardTabPresentation.ClipRegion> visibleRegions = [];
    private readonly List<MarketBoardTabPresentation.ClipRegion> scratch = [];

    internal void BeginFrame() => drawLists.Clear();

    internal void Capture(ImDrawListPtr drawList)
    {
        if (!drawList.IsNull && !drawLists.Contains(drawList))
            drawLists.Add(drawList);
    }

    internal unsafe void FinishFrame(IReadOnlyList<MarketBoardTabPresentation.ClipRegion> occlusions)
    {
        if (occlusions.Count == 0)
            return;

        // Called only after WindowHost's ImGui.End, including table channel merges.
        // Duplicates reference the same geometry and retain texture/index offsets;
        // each receives a disjoint scissor rectangle outside native foreground windows.
        foreach (var drawList in drawLists)
        {
            ref var commands = ref drawList.CmdBuffer;
            clippedCommands.Clear();
            for (var index = 0; index < commands.Size; index++)
            {
                var command = commands[index];
                if (command.ElemCount == 0 || command.UserCallback != null)
                {
                    clippedCommands.Add(command);
                    continue;
                }

                var clip = command.ClipRect;
                MarketBoardTabPresentation.FillVisibleRegions(
                    new Vector2(clip.X, clip.Y), new Vector2(clip.Z - clip.X, clip.W - clip.Y),
                    occlusions, visibleRegions, scratch);
                foreach (var region in visibleRegions)
                {
                    var clipped = command;
                    clipped.ClipRect = new Vector4(region.Minimum, region.Maximum.X, region.Maximum.Y);
                    clippedCommands.Add(clipped);
                }
            }
            commands.Resize(clippedCommands.Count);
            for (var index = 0; index < clippedCommands.Count; index++)
                commands[index] = clippedCommands[index];
        }
    }
}
