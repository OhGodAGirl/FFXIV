namespace BetterGlamorPlates.Features.GlamourPlates;

internal sealed record DyeReagentFact(uint ItemId, string Name);
internal sealed record DyeRowFact(uint Id, byte Shade, byte SubOrder, string Name, uint Color, IReadOnlyList<DyeReagentFact> Reagents);
internal sealed record DyeInventoryFact(uint Container, ushort Slot, uint ItemId, long Quantity);

/// <summary>Projects sheet facts and bounded carried-inventory observations before allocation.</summary>
internal static class DyePreparation
{
    internal static DyeOption[] Options(IEnumerable<DyeRowFact> rows)
        => rows.Where(row => CanReadDyeRow(row.Id, row.Shade, string.IsNullOrEmpty(row.Name)))
            .OrderBy(row => row.Shade).ThenBy(row => row.SubOrder).Take(255)
            .Select(row => new DyeOption((byte)row.Id, row.Name, row.Color,
                !row.Reagents.Any(item => item.ItemId == 52254),
                string.Join(" / ", row.Reagents.Where(item => item.ItemId != 0).Select(item => item.Name)))).ToArray();

    internal static bool CanReadDyeRow(uint id, byte shade, bool nameEmpty)
        => id is > 0 and <= byte.MaxValue && shade != 0 && !nameEmpty;

    internal static Dictionary<uint, List<byte>> ReagentMap(PlateLoadPlan plan, Func<byte, IEnumerable<uint>> readReagents)
    {
        var wanted = plan.Steps.Where(step => !step.IsSkipped && step.SavedItem is not null && step.Source is not null)
            .SelectMany(step => new[] { step.SavedItem!.Stain0Id, step.SavedItem.Stain1Id }).Distinct();
        var result = new Dictionary<uint, List<byte>>();
        foreach (var id in wanted)
            foreach (var reagent in readReagents(id))
                if (reagent != 0)
                {
                    if (!result.TryGetValue(reagent, out var colours)) result[reagent] = colours = [];
                    if (!colours.Contains(id)) colours.Add(id);
                }
        return result;
    }

    internal static bool CanReadInventory(bool loaded, int size) => loaded && size is >= 0 and <= 35;

    internal static List<DyeSupply> Supplies(IReadOnlyDictionary<uint, List<byte>> reagents, IEnumerable<DyeInventoryFact> inventory)
    {
        var supplies = new List<DyeSupply>(140);
        foreach (var item in inventory)
            if (reagents.TryGetValue(item.ItemId, out var colours) && item.Quantity > 0)
                foreach (var dyeId in colours)
                    supplies.Add(new(dyeId, item.ItemId, item.Container, item.Slot, (uint)item.Quantity));
        return supplies;
    }
}
