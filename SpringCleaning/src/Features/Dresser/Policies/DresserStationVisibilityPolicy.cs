namespace SpringCleaning;

internal static class DresserStationVisibilityPolicy
{
    public static bool ShouldShowCompanions(
        bool dresserAddonVisible,
        bool glamourPlateEditorVisible) =>
        dresserAddonVisible && !glamourPlateEditorVisible;
}
