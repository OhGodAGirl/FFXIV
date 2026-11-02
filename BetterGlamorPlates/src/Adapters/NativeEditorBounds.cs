using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;
using Dalamud.Plugin.Services;

namespace BetterGlamorPlates.Adapters;

/// <summary>Copies visible native geometry on the framework thread without retaining pointers.</summary>
internal sealed class NativeEditorBounds(IGameGui gameGui)
{
    internal unsafe WindowBounds? Read()
    {
        var addon = gameGui.GetAddonByName(NativePlateEditor.AddonName);
        if (addon.IsNull || !addon.IsVisible)
            return null;
        var unit = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)addon.Address;
        return new WindowBounds(unit->X, unit->Y, unit->GetScaledWidth(true), unit->GetScaledHeight(true));
    }
}
