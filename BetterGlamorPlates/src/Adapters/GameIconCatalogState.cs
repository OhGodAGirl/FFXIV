using BetterGlamorPlates.Features.GlamourPlates;

namespace BetterGlamorPlates.Adapters;

internal readonly record struct GameIconFact(uint Id, string Name, bool Weapon, bool Armour, bool Accessory);

/// <summary>Owns bounded incremental enumeration independently of the game sheet.</summary>
internal sealed class GameIconCatalogState : IDisposable
{
    internal const int RowsPerUpdate = 256;
    internal const int MaximumRows = 100000;
    internal const int MaximumIcons = 65536;
    private readonly Func<IEnumerator<GameIconFact>> open;
    private readonly Dictionary<uint, GameIconOption> choices = [];
    private IEnumerator<GameIconFact>? rows;
    private int rowsRead;
    internal bool Complete { get; private set; }
    internal IReadOnlyList<GameIconOption> Icons { get; private set; } = [];

    internal GameIconCatalogState(Func<IEnumerator<GameIconFact>> open) => this.open = open;

    internal void Advance()
    {
        if (Complete) return;
        rows ??= open();
        for (var index = 0; index < RowsPerUpdate; index++)
        {
            if (rowsRead >= MaximumRows || choices.Count >= MaximumIcons || !rows.MoveNext())
            {
                Icons = choices.Values.OrderBy(icon => icon.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(icon => icon.Id).ToArray();
                Complete = true;
                rows.Dispose();
                rows = null;
                return;
            }
            rowsRead++;
            var item = rows.Current;
            if (item.Id == 0 || item.Name.Length == 0) continue;
            var category = item.Weapon ? "Weapons" : item.Armour ? "Armour" : item.Accessory ? "Accessories" : "Other";
            choices.TryAdd(item.Id, new GameIconOption(item.Id, item.Name, category));
        }
    }

    internal static bool IsValid(uint id, Func<string, bool> exists)
        => id is > 0 and <= 999999 && exists($"ui/icon/{id / 1000 * 1000:D6}/{id:D6}.tex");

    public void Dispose()
    {
        Complete = true;
        rows?.Dispose();
        rows = null;
        choices.Clear();
        Icons = [];
    }
}

