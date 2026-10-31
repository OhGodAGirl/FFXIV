using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Features.CollectionData.Capture;

public sealed record NotebookSeries(uint RowId, ImmutableArray<uint> Contents);
public readonly record struct NotebookSlot(int Series, int Slot);

public static class NotebookMappingPolicy
{
    public static NotebookSlot Resolve(uint id, IEnumerable<NotebookSeries> seriesRows, int nativeSeriesCount, int nativeSlotCount)
    {
        var seriesIndex = 0;
        foreach (var series in seriesRows)
        {
            if (series.RowId == 0) continue;
            if (seriesIndex >= nativeSeriesCount) throw new SourceReadException(ReasonCode.MappingValidationFailed);
            for (var slot = 0; slot < series.Contents.Length; slot++)
                if (series.Contents[slot] == id)
                {
                    if (slot >= nativeSlotCount) throw new SourceReadException(ReasonCode.MappingValidationFailed);
                    return new(seriesIndex, slot);
                }
            seriesIndex++;
        }
        throw new SourceReadException(ReasonCode.MappingValidationFailed);
    }

    public static QuestState Quest(uint id, bool completed, bool accepted, byte? sequence) =>
        new(id, completed ? QuestStatus.Completed : accepted ? QuestStatus.InProgress : QuestStatus.NotStarted,
            accepted ? sequence : null);
}
