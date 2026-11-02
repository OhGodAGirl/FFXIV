using Dalamud.Bindings.ImGui;

namespace SpringCleaning;

internal readonly record struct UiDrawObservation(int Frame, float Alpha)
{
    public static UiDrawObservation Capture() =>
        new(ImGui.GetFrameCount(), ImGui.GetStyle().Alpha);
}
