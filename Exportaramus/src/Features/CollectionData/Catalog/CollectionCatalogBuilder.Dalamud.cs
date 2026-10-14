using Exportaramus.Features.Diagnostics;
using System.Collections.Immutable;
using System.Diagnostics;
using Dalamud.Plugin.Services;
using Exportaramus.Features.CollectionData.State;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace Exportaramus.Features.CollectionData.Catalog;

// Sheet projections run in bounded framework slices. No live row survives a yielded projection.
public sealed class CollectionCatalogBuilder : ICatalogProjectionBuilder
{
    private readonly IDataManager data;
    private readonly DerivedCatalogInputs derived;
    private readonly BoundedCatalogProjection projection;

    public CollectionCatalogBuilder(IDataManager data, DerivedCatalogInputs derived, DiagnosticMeasurements? measurements = null)
    {
        this.data = data;
        this.derived = derived;
        projection = new(data.Language.ToString(), data.GameData.Repositories.Values.Single(r => r.ExpansionId == 0).Version,
            ProjectRows().GetEnumerator(), () => (data.Language.ToString(), data.GameData.Repositories.Values.Single(r => r.ExpansionId == 0).Version),
            () => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency, measurements);
    }

    public CatalogProjection? Projection => projection.Projection;
    // Keep the one-millisecond budget; allow cheap rows to use that budget instead of stopping at 256.
    public bool Advance(int maximumRows = 2048) => projection.Advance(maximumRows);
    public void Dispose() => projection.Dispose();

    private IEnumerable<(string, CatalogItem)> ProjectRows()
    {
        foreach (var r in Sheet<Item>()) yield return CatalogRowPolicy.Project(new("@items", r.RowId, r.Name.ToString()));
        foreach (var r in Sheet<Mount>()) yield return CatalogRowPolicy.Project(new("mounts", r.RowId, r.Singular.ToString(), Order: r.Order, Icon: r.Icon));
        foreach (var r in Sheet<Companion>()) yield return CatalogRowPolicy.Project(new("minions", r.RowId, r.Singular.ToString(), Icon: r.Icon));
        foreach (var r in Sheet<Emote>()) yield return CatalogRowPolicy.Project(new("emotes", r.RowId, r.Name.ToString(), LinkId: r.TextCommand.RowId));
        foreach (var r in Sheet<BuddyEquip>()) yield return CatalogRowPolicy.Project(new("bardings", r.RowId, r.Name.ToString()));
        foreach (var r in Sheet<TripleTriadCard>()) yield return CatalogRowPolicy.Project(new("cards", r.RowId, r.Name.ToString()));
        foreach (var r in Sheet<ENpcBase>())
        {
            foreach (var link in r.ENpcData)
                yield return CatalogRowPolicy.Project(new("npcs", link.RowId, Sheet<ENpcResident>().GetRowOrDefault(r.RowId)?.Singular.ToString(), LinkedRowExists: Sheet<TripleTriad>().HasRow(link.RowId)));
            yield return ("", new(0, 0, "", false));
        }
        foreach (var r in Sheet<Glasses>())
            if (r.RowId != 0) yield return ("facewear", FacewearMappingPolicy.Project(r.RowId, r.Style.RowId));
        // No experimental XBMPet columns are read. Canonical IDs are pinned to the
        // complete row directory and native manager's 50-pet domain; capture validates it.
        for (uint id = 1; id <= Capture.BeastReadPolicy.CatalogCount; id++)
            yield return ("beasts", new(id, id, $"Beast {id}", true));
        foreach (var r in Sheet<Ornament>()) yield return CatalogRowPolicy.Project(new("fashions", r.RowId, r.Singular.ToString()));
        foreach (var r in Sheet<BannerBg>()) yield return CatalogRowPolicy.Project(new("frames", r.RowId, r.Name.ToString(), LinkId: r.UnlockCondition.RowId));
        yield return CatalogRowPolicy.Project(new("frames", 573, null, LinkId: 573));
        foreach (var r in Sheet<MKDLore>()) yield return CatalogRowPolicy.Project(new("occult_records", r.RowId, r.Name.ToString()));
        foreach (var r in Sheet<Orchestrion>()) yield return CatalogRowPolicy.Project(new("orchestrions", r.RowId, r.Name.ToString()));
        foreach (var r in Sheet<AozAction>()) yield return CatalogRowPolicy.Project(new("spells", r.RowId, r.Action.RowId == 0 ? null : r.Action.Value.Name.ToString(), LinkId: r.Action.RowId));
        foreach (var r in Sheet<Aetheryte>()) yield return CatalogRowPolicy.Project(new("aetherytes", r.RowId, r.PlaceName.Value.Name.ToString(), IsAetheryte: r.IsAetheryte, Invisible: r.Invisible));
        foreach (var r in Sheet<ChocoboTaxiStand>()) yield return CatalogRowPolicy.Project(new("chocobo_routes", r.RowId, r.PlaceName.ToString()));
        foreach (var r in Sheet<Leve>()) yield return CatalogRowPolicy.Project(new("leves", r.RowId, r.Name.ToString()));
        foreach (var r in Sheet<CharaMakeCustomize>()) yield return CatalogRowPolicy.Project(new("hairstyles", r.RowId, r.HintItem.RowId == 0 ? null : r.HintItem.Value.Name.ToString(), LinkId: r.UnlockLink, ItemId: r.HintItem.RowId));
        foreach (var r in Sheet<MYCWarResultNotebook>()) yield return CatalogRowPolicy.Project(new("field_records", r.RowId, r.Name.ToString(), LinkId: r.Number));
        foreach (var r in Sheet<VVDNotebookContents>()) yield return CatalogRowPolicy.Project(new("survey_records", r.RowId, r.Name.ToString()));
        foreach (var r in Sheet<Quest>()) yield return CatalogRowPolicy.Project(new("quests", r.RowId, r.Name.ToString()));
        foreach (var r in Sheet<Achievement>()) yield return CatalogRowPolicy.Project(new("achievements", r.RowId, r.Name.ToString()));
        foreach (var r in Sheet<Cabinet>()) yield return CatalogRowPolicy.Project(new("armoires", r.RowId, r.Item.RowId == 0 ? null : r.Item.Value.Name.ToString(), ItemId: r.Item.RowId, Order: r.Order));
        foreach (var r in derived.Outfits) yield return ("outfits", CatalogRowPolicy.WithItemName(r, Sheet<Item>().GetRow(r.Id).Name.ToString()));
        foreach (var r in derived.Relics) yield return ("relics", CatalogRowPolicy.WithItemName(r, Sheet<Item>().GetRow(r.Id).Name.ToString()));
    }

    private ExcelSheet<T> Sheet<T>() where T : struct, IExcelRow<T> => data.GetExcelSheet<T>();
}
