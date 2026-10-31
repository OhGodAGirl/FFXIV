using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Features.CollectionData.Capture;

public static class FieldRecordMappingPolicy
{
    public static ImmutableDictionary<uint, uint> CreateMap(IEnumerable<(uint RecordId, uint ItemId)> rows)
    {
        var result = ImmutableDictionary.CreateBuilder<uint, uint>();
        var items = new HashSet<uint>();
        foreach (var (recordId, itemId) in rows)
        {
            if (recordId is < 1 or > 50 || itemId == 0 || !items.Add(itemId) || !result.TryAdd(recordId, itemId))
                throw new SourceReadException(ReasonCode.MappingValidationFailed);
        }
        if (result.Count != 50) throw new SourceReadException(ReasonCode.MappingValidationFailed);
        return result.ToImmutable();
    }

    public static bool ReadResult(long result) => result switch
    {
        1 => true,
        2 => false,
        3 => throw new SourceReadException(ReasonCode.NotLoadedThisSession),
        _ => throw new SourceReadException(ReasonCode.MappingValidationFailed),
    };
}
