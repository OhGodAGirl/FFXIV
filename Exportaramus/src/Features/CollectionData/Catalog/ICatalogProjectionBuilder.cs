namespace Exportaramus.Features.CollectionData.Catalog;

public interface ICatalogProjectionBuilder : IDisposable
{
    CatalogProjection? Projection { get; }
    bool Advance(int maximumRows = 256);
}
