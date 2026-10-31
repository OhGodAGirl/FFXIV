using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Features.CollectionData.Catalog;



public static class FacewearMappingPolicy
{
    public static CatalogItem Project(uint glassesId, uint styleId)
    {
        if (glassesId == 0 || glassesId > ushort.MaxValue || styleId == uint.MaxValue)
            throw new SourceReadException(ReasonCode.MappingValidationFailed);
        uint id = checked(styleId + 1);
        return new(id, styleId, $"Facewear {id}", true);
    }

    // Validate every mapped row, including variants after an owned variant.
    public static bool Read(uint id, IEnumerable<FacewearRow> rows, Func<ushort, bool> isUnlocked)
    {
        bool found = false, owned = false;
        foreach (var row in rows)
        {
            if (row.GlassesId == 0) continue;
            var projected = Project(row.GlassesId, row.StyleId);
            if (projected.Id != id) continue;
            found = true;
            owned |= isUnlocked(checked((ushort)row.GlassesId));
        }
        if (!found) throw new SourceReadException(ReasonCode.MappingValidationFailed);
        return owned;
    }
}

