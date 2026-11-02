using BetterGlamorPlates.Features.GlamourPlates;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace BetterGlamorPlates.Adapters;

/// <summary>Maps game sheet rows; bounded enumeration is owned by the independent catalog state.</summary>
internal sealed class GameIconCatalog : IDisposable
{
    internal const int RowsPerUpdate = GameIconCatalogState.RowsPerUpdate;
    internal const int MaximumRows = GameIconCatalogState.MaximumRows;
    internal const int MaximumIcons = GameIconCatalogState.MaximumIcons;
    private readonly IDataManager dataManager;
    private readonly GameIconCatalogState state;
    internal bool Complete => state.Complete;
    internal IReadOnlyList<GameIconOption> Icons => state.Icons;

    internal GameIconCatalog(IDataManager dataManager)
    {
        this.dataManager = dataManager;
        state = new GameIconCatalogState(() => ReadRows().GetEnumerator());
    }

    private IEnumerable<GameIconFact> ReadRows()
    {
        foreach (var item in dataManager.GetExcelSheet<Item>())
        {
            if (item.Icon == 0 || item.Name.IsEmpty)
            {
                yield return new GameIconFact(item.Icon, item.Name.ToString(), false, false, false);
                continue;
            }
            var slots = item.EquipSlotCategory.Value;
            yield return new GameIconFact(item.Icon, item.Name.ToString(),
                slots.MainHand > 0 || slots.OffHand > 0,
                slots.Head > 0 || slots.Body > 0 || slots.Gloves > 0 || slots.Legs > 0 || slots.Feet > 0,
                slots.Ears > 0 || slots.Neck > 0 || slots.Wrists > 0 || slots.FingerR > 0 || slots.FingerL > 0);
        }
    }

    internal void Advance() => state.Advance();
    internal bool IsValid(uint id) => GameIconCatalogState.IsValid(id, dataManager.FileExists);
    public void Dispose() => state.Dispose();
}

