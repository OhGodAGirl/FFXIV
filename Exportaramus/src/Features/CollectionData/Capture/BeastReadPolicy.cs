using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Features.CollectionData.Capture;

public static class BeastReadPolicy
{
    public const int CatalogCount = 50;

    public static void ValidateCatalog(int rowCount, bool hasZeroRow, Func<uint, bool> hasRow)
    {
        if (rowCount != CatalogCount + 1 || !hasZeroRow)
            throw new SourceReadException(ReasonCode.MappingValidationFailed);
        for (uint id = 1; id <= CatalogCount; id++)
            if (!hasRow(id)) throw new SourceReadException(ReasonCode.MappingValidationFailed);
    }

    public static ImmutableHashSet<uint> Read(bool received, int count, Func<uint, bool> isUnlocked, Func<bool> unchanged)
    {
        if (!received) throw new SourceReadException(ReasonCode.NotLoadedThisSession);
        if (count is < 0 or > CatalogCount) throw new SourceReadException(ReasonCode.CaptureFailed);
        var ids = ImmutableHashSet.CreateBuilder<uint>();
        for (uint id = 1; id <= CatalogCount; id++)
            if (isUnlocked(id)) ids.Add(id);
        if (!unchanged() || ids.Count != count)
            throw new SourceReadException(ReasonCode.CaptureFailed);
        return ids.ToImmutable();
    }
}
