using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SpringCleaning;

// Native controls are stateless. The controller still owns original values,
// source selection, timing and restoration; NativeUi owns action dispatch.
internal static class DresserNativeControls
{
    internal static unsafe bool EnsureGearsetItemsVisible(IGameGui gameGui, out bool changed)
    {
        changed = false;
        var address = gameGui.GetAddonByName("MiragePrismPrismBoxCrystallize");
        if (address.IsNull || !address.IsVisible)
            return false;

        var addon = (AddonMiragePrismPrismBoxCrystallize*)address.Address;
        var checkbox = addon->GearsetFilterCheckbox;
        if (checkbox == null)
            return false;
        if (!checkbox->IsChecked)
            return true;
        if (!checkbox->AtkComponentButton.IsEnabled)
            return false;

        checkbox->SetChecked(false);
        if (!NativeUi.DispatchButtonClick(&checkbox->AtkComponentButton))
        {
            checkbox->SetChecked(true);
            return false;
        }

        changed = true;
        return true;
    }

    internal static unsafe bool TryGetGearsetItemsHidden(IGameGui gameGui, out bool hidden)
    {
        hidden = false;
        var address = gameGui.GetAddonByName("MiragePrismPrismBoxCrystallize");
        if (address.IsNull || !address.IsVisible)
            return false;

        var addon = (AddonMiragePrismPrismBoxCrystallize*)address.Address;
        if (addon->GearsetFilterCheckbox == null)
            return false;

        hidden = addon->GearsetFilterCheckbox->IsChecked;
        return true;
    }

    internal static unsafe bool SetGearsetItemsHidden(IGameGui gameGui, bool hidden)
    {
        var address = gameGui.GetAddonByName("MiragePrismPrismBoxCrystallize");
        if (address.IsNull || !address.IsVisible)
            return false;

        var addon = (AddonMiragePrismPrismBoxCrystallize*)address.Address;
        var checkbox = addon->GearsetFilterCheckbox;
        if (checkbox == null)
            return false;
        if (checkbox->IsChecked == hidden)
            return true;
        if (!checkbox->AtkComponentButton.IsEnabled)
            return false;

        checkbox->SetChecked(hidden);
        if (NativeUi.DispatchButtonClick(&checkbox->AtkComponentButton))
            return true;

        checkbox->SetChecked(!hidden);
        return false;
    }

    internal static unsafe bool TryGetDresserCategoryIndex(IGameGui gameGui, out int categoryIndex)
    {
        categoryIndex = -1;
        var address = gameGui.GetAddonByName("MiragePrismPrismBoxCrystallize");
        if (address.IsNull || !address.IsVisible)
            return false;

        var addon = (AddonMiragePrismPrismBoxCrystallize*)address.Address;
        if (addon->CategoryDropDown == null)
            return false;

        categoryIndex = addon->CategoryDropDown->GetSelectedItemIndex();
        return categoryIndex >= 0;
    }

    internal static unsafe bool TryGetArmouryChestCategoryIndexes(IGameGui gameGui, out IReadOnlyList<int> categoryIndexes)
    {
        categoryIndexes = Array.Empty<int>();
        var address = gameGui.GetAddonByName("MiragePrismPrismBoxCrystallize");
        if (address.IsNull || !address.IsVisible)
            return false;

        var addon = (AddonMiragePrismPrismBoxCrystallize*)address.Address;
        var labels = addon->CategoryLabels;
        var matches = new List<int>();
        for (var index = 0; index < labels.Length; index++)
        {
            var label = labels[index].ExtractText();
            if (!label.Contains("Armoury", StringComparison.OrdinalIgnoreCase))
                continue;

            matches.Add(index);
        }

        var populatedArmouryIndexes = new List<int>();
        for (var index = NativeUi.ArmouryChestDresserCategory;
             index < labels.Length && populatedArmouryIndexes.Count < NativeUi.ArmouryChestDresserCategoryCount;
             index++)
        {
            if (!string.IsNullOrWhiteSpace(labels[index].ExtractText()))
                populatedArmouryIndexes.Add(index);
        }
        if (populatedArmouryIndexes.Count != NativeUi.ArmouryChestDresserCategoryCount)
            return false;

        // All five native Armoury Chest slot groups must be present before the
        // controller begins its sweep. English labels identify them directly;
        // the populated native order is the localization-safe fallback.
        categoryIndexes = matches.Count == NativeUi.ArmouryChestDresserCategoryCount
            ? matches
            : populatedArmouryIndexes;
        return true;
    }

    internal static unsafe bool EnsureDresserCategory(IGameGui gameGui, int desiredCategoryIndex, out bool changed)
    {
        changed = false;
        var address = gameGui.GetAddonByName("MiragePrismPrismBoxCrystallize");
        if (address.IsNull || !address.IsVisible)
            return false;

        var addon = (AddonMiragePrismPrismBoxCrystallize*)address.Address;
        if (addon->CategoryDropDown == null
            || addon->CategoryPrevButton == null
            || addon->CategoryNextButton == null)
            return false;

        var currentCategoryIndex = addon->CategoryDropDown->GetSelectedItemIndex();
        var categoryCount = addon->CategoryLabels.Length;
        if (currentCategoryIndex < 0
            || desiredCategoryIndex < 0
            || desiredCategoryIndex >= categoryCount)
            return false;
        if (currentCategoryIndex == desiredCategoryIndex)
            return true;
        var forwardDistance = (desiredCategoryIndex - currentCategoryIndex + categoryCount) % categoryCount;
        var backwardDistance = (currentCategoryIndex - desiredCategoryIndex + categoryCount) % categoryCount;
        var button = backwardDistance < forwardDistance
            ? addon->CategoryPrevButton
            : addon->CategoryNextButton;
        if (!button->IsEnabled)
            return false;

        changed = NativeUi.DispatchButtonClick(button);
        return changed;
    }

    internal static unsafe bool AlignGlamourCreation(IGameGui gameGui, float x, float y)
    {
        var address = gameGui.GetAddonByName("MiragePrismPrismBoxCrystallize");
        if (address.IsNull || !address.IsVisible)
            return false;

        var addon = (AtkUnitBase*)address.Address;
        var targetX = (short)Math.Clamp((int)MathF.Round(x), short.MinValue, short.MaxValue);
        var targetY = (short)Math.Clamp((int)MathF.Round(y), short.MinValue, short.MaxValue);
        if (addon->X != targetX || addon->Y != targetY)
            addon->SetPosition(targetX, targetY);
        return true;
    }

}
