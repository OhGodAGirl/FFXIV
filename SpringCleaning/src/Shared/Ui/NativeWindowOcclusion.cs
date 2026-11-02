using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SpringCleaning;

internal static unsafe class NativeWindowOcclusion
{
    public static IReadOnlyList<NativeWindowOcclusionPolicy.Region> GetAbove(
        AtkUnitBase* anchor,
        NativeWindowOcclusionPolicy.Region rail,
        bool anchorHasFocus)
    {
        List<NativeWindowOcclusionPolicy.Region> occlusions = [];
        var manager = RaptureAtkUnitManager.Instance();
        if (anchor == null || manager == null)
            return occlusions;

        var rawFocusedAddon = manager->AtkUnitManager.FocusedAddon;
        var focusedAddon = rawFocusedAddon == null
            ? null
            : rawFocusedAddon->GetUnitBaseForFocus();
        if (focusedAddon == null)
            focusedAddon = rawFocusedAddon;

        TryAddOcclusion(rawFocusedAddon, anchor, rail, occlusions);
        if (focusedAddon != rawFocusedAddon)
            TryAddOcclusion(focusedAddon, anchor, rail, occlusions);

        ref var loaded = ref manager->AtkUnitManager.AllLoadedUnitsList;
        var entries = loaded.Entries;
        var count = Math.Min((int)loaded.Count, entries.Length);
        for (var index = 0; index < count; index++)
        {
            var candidate = entries[index].Value;
            if (candidate == null
                || candidate == anchor
                || candidate == rawFocusedAddon
                || candidate == focusedAddon)
            {
                continue;
            }
            TryAddOcclusion(candidate, anchor, rail, occlusions);
        }

        return occlusions;
    }

    private static void TryAddOcclusion(
        AtkUnitBase* candidate,
        AtkUnitBase* anchor,
        NativeWindowOcclusionPolicy.Region rail,
        List<NativeWindowOcclusionPolicy.Region> occlusions)
    {
        if (candidate == null || candidate == anchor || !IsInFront(anchor, candidate))
            return;

        var opacity = NativeWindowOcclusionPolicy.GetOpacity(
            candidate->Alpha,
            candidate->VisibilityState.HasFlag(
                AtkUnitBaseVisibilityState.Hide),
            candidate->AppliedVisibilityState.HasFlag(
                AtkUnitBaseVisibilityState.Hide));
        if (!NativeWindowOcclusionPolicy.ShouldOccludeNativeWindow(
                candidate == anchor,
                candidate->IsVisible,
                candidate->RootNode != null,
                candidate->WindowCollisionNode != null
                    || candidate->WindowHeaderCollisionNode != null
                    || candidate->WindowNode != null,
                opacity))
        {
            return;
        }

        var position = new Vector2(candidate->X, candidate->Y);
        var size = new Vector2(
            candidate->GetScaledWidth(true),
            candidate->GetScaledHeight(true));
        var candidateRegion = new NativeWindowOcclusionPolicy.Region(
            position,
            position + size);
        if (NativeWindowOcclusionPolicy.Overlaps(rail, candidateRegion))
            occlusions.Add(candidateRegion);
    }

    private static bool IsInFront(AtkUnitBase* anchor, AtkUnitBase* candidate)
    {
        var manager = RaptureAtkUnitManager.Instance();
        if (manager == null)
            return true;
        // Focus does not override depth or the live same-layer draw order.
        var layers = manager->AtkUnitManager.DepthLayers;
        if (candidate->DepthLayer == anchor->DepthLayer && anchor->DepthLayer < layers.Length)
        {
            ref var layer = ref layers[(int)anchor->DepthLayer];
            var anchorIndex = -1;
            var candidateIndex = -1;
            for (var i = 0; i < Math.Min((int)layer.Count, layer.Entries.Length); i++)
            {
                var unit = layer.Entries[i].Value;
                if (unit == anchor) anchorIndex = i;
                if (unit == candidate) candidateIndex = i;
            }
            if (anchorIndex >= 0 && candidateIndex >= 0)
                return NativeWindowOcclusionPolicy.IsInFront(0, (ushort)anchorIndex, 0, (ushort)candidateIndex);
        }
        return NativeWindowOcclusionPolicy.IsInFront(anchor->DepthLayer, anchor->DrawOrderIndex,
            candidate->DepthLayer, candidate->DrawOrderIndex);
    }
}
