using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SpringCleaning;

internal sealed unsafe class NativeWindowFocusTracker : IDisposable
{
    private readonly IAddonLifecycle addonLifecycle;
    private nint focusedAddress;
    private nint focusedBaseAddress;
    private string focusedName = string.Empty;
    private bool disposed;

    public NativeWindowFocusTracker(IAddonLifecycle addonLifecycle)
    {
        this.addonLifecycle = addonLifecycle;
        addonLifecycle.RegisterListener(AddonEvent.PostFocusChanged, OnFocusChanged);
    }

    public bool IsFocused(AtkUnitBase* anchor)
    {
        if (anchor == null)
            return false;

        var manager = RaptureAtkUnitManager.Instance();
        var rawFocused = manager == null
            ? null
            : manager->AtkUnitManager.FocusedAddon;
        var resolvedFocused = rawFocused == null
            ? null
            : rawFocused->GetUnitBaseForFocus();
        if (resolvedFocused == null)
            resolvedFocused = rawFocused;

        return NativeWindowFocusPolicy.IsAnchorFocused(
            (nint)anchor,
            anchor->NameString,
            focusedAddress,
            focusedBaseAddress,
            focusedName,
            (nint)rawFocused,
            (nint)resolvedFocused);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        addonLifecycle.UnregisterListener(AddonEvent.PostFocusChanged, OnFocusChanged);
    }

    private void OnFocusChanged(AddonEvent _, AddonArgs args)
    {
        if (args is not AddonFocusChangedArgs focus)
            return;

        var address = focus.Addon.Address;
        if (focus.ShouldFocus)
        {
            var addon = (AtkUnitBase*)address;
            focusedAddress = address;
            focusedBaseAddress = addon == null
                ? 0
                : (nint)addon->GetUnitBaseForFocus();
            focusedName = focus.AddonName;
        }
        else if (focusedAddress == address)
        {
            focusedAddress = 0;
            focusedBaseAddress = 0;
            focusedName = string.Empty;
        }
    }
}
