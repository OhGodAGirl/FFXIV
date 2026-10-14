using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticPipeline
{
    public DiagnosticCatalog CatalogCache { get; init; } = new(null, "Empty", 0);
    public long AcceptedCaptureGeneration { get; init; }
    public ImmutableDictionary<string, string?> Fingerprints { get; init; } = ImmutableDictionary<string, string?>.Empty;
    public DiagnosticSerializers Serializers { get; init; } = new("NotBuilt", "NotBuilt", "NotBuilt");
    public ImmutableArray<DiagnosticOmission> Omissions { get; init; } = [];
    public DiagnosticOutputBytes OutputBytes { get; init; } = new(0, 0, 0);
    public DiagnosticCollect FfxivCollect { get; init; } = new();
}
