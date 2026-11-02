using System.Text.Json.Serialization;

namespace CollectionShopper;

internal sealed record CollectionCatalogResult(
    IReadOnlyList<Collectable> Minions,
    IReadOnlyList<Collectable> Mounts,
    IReadOnlyList<Collectable> Orchestrions,
    IReadOnlyList<Collectable> Hairstyles,
    IReadOnlyList<Collectable> Facewear,
    IReadOnlyList<Collectable> Fashions);

