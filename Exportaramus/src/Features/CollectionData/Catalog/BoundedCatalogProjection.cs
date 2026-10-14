using Exportaramus.Features.Diagnostics;
using System.Collections.Immutable;

namespace Exportaramus.Features.CollectionData.Catalog;

public sealed class BoundedCatalogProjection : ICatalogProjectionBuilder
{
    private readonly string language;
    private readonly DiagnosticMeasurements measurements;
    private readonly string version;
    private readonly IEnumerator<(string Key, CatalogItem Item)> rows;
    private readonly Func<(string Language, string Version)> identity;
    private readonly Func<double> milliseconds;
    private readonly ImmutableArray<(string Key, CatalogItem Item)>.Builder items = ImmutableArray.CreateBuilder<(string, CatalogItem)>();
    private readonly ImmutableArray<uint>.Builder knownItems = ImmutableArray.CreateBuilder<uint>();
    private bool finished;

    public BoundedCatalogProjection(string language, string version, IEnumerator<(string Key, CatalogItem Item)> rows,
        Func<(string Language, string Version)> identity, Func<double> milliseconds, DiagnosticMeasurements? measurements = null)
    {
        if (string.IsNullOrWhiteSpace(version)) throw new InvalidOperationException("Game data version is unavailable.");
        this.language = language; this.version = version; this.rows = rows; this.identity = identity; this.milliseconds = milliseconds; this.measurements = measurements ?? new();
    }
    public CatalogProjection? Projection { get; private set; }

    public bool Advance(int maximumRows = 256) => measurements.Measure("CatalogProjectionSlice", () => AdvanceCore(maximumRows));
    private bool AdvanceCore(int maximumRows)
    {
        if (finished) return true;
        if (maximumRows is < 1 or > 2048) throw new ArgumentOutOfRangeException(nameof(maximumRows));
        var current = identity();
        if (current.Language != language || current.Version != version)
            throw new InvalidOperationException("Game data changed during catalog projection.");
        var start = milliseconds();
        for (var i = 0; i < maximumRows; i++)
        {
            if (!rows.MoveNext())
            {
                Projection = new(language, version, CatalogRowPolicy.Definitions, items.ToImmutable(), knownItems.ToImmutable());
                finished = true; return true;
            }
            var entry = rows.Current;
            if (entry.Key == "@items")
            {
                knownItems.Add(entry.Item.Id);
                // Keep display metadata for the dynamic Dresser collection; publication selects only observed IDs.
                items.Add(("glamour_dresser", entry.Item));
            }
            else if (entry.Item.Id != 0) items.Add(entry);
            if (milliseconds() - start >= 1) break;
        }
        return false;
    }
    public void Dispose() { rows.Dispose(); finished = true; }
}
