using System.Collections.Immutable;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace Exportaramus.Features.GuidedExport;

public sealed record GuidedRuntimeValidation(string GameDataVersion, ObjectKind ObjectKind,
    ImmutableHashSet<uint> DresserBaseIds, ImmutableHashSet<uint> ArmoireBaseIds,
    string DresserAddon, string ArmoireAddon, float InteractionRange,
    ImmutableHashSet<ConditionFlag> UnsafeConditions, bool SupervisedValidationComplete)
{
    public bool DevelopmentCompatibility { get; init; }
    public static GuidedRuntimeValidation Development(IDataManager data)
    {
        var names = data.GetExcelSheet<EObjName>(ClientLanguage.English);
        return new(Exportaramus.Features.CollectionData.Capture.ReaderCompatibility.CurrentGameVersion, ObjectKind.EventObj,
            names.Where(row => row.Singular.ExtractText().Equals("Glamour Dresser", StringComparison.OrdinalIgnoreCase)).Select(row => row.RowId).ToImmutableHashSet(),
            names.Where(row => row.Singular.ExtractText().Equals("Armoire", StringComparison.OrdinalIgnoreCase)).Select(row => row.RowId).ToImmutableHashSet(),
            "MiragePrismPrismBox", "Cabinet", 2.5f,
            [ConditionFlag.InCombat, ConditionFlag.BetweenAreas, ConditionFlag.BetweenAreas51, ConditionFlag.WatchingCutscene, ConditionFlag.OccupiedInCutSceneEvent, ConditionFlag.Mounted], false)
        { DevelopmentCompatibility = true };
    }
}
