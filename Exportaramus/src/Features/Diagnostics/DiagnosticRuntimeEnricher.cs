using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

/// <summary>Combines measured immutable metadata, retaining explicit incompleteness for unobserved domains.</summary>
public static class DiagnosticRuntimeEnricher
{
    public static DiagnosticReportInput Apply(DiagnosticReportInput input, DiagnosticRuntime runtime,
        DiagnosticLifecycle lifecycle, DiagnosticOperations operations, DiagnosticStorage storage,
        ImmutableDictionary<string, double> latestMilliseconds, bool unrepresentableSamples, DiagnosticUi? observedUi = null)
    {
        var incomplete = ImmutableArray.CreateBuilder<string>();
        if (observedUi is null) incomplete.Add("ui");
        if (runtime.FfxivClientVersion == "Unavailable") incomplete.Add("runtime");
        if (storage.LastWrite.Status == "Never" || !storage.CanonicalAvailable) incomplete.Add("storage");
        if (input.Sources.Any(source => source.SourceStatus is "Fresh" or "Stale" && !latestMilliseconds.ContainsKey("Source." + source.SourceKey))) incomplete.Add("sourceHealth");
        if (unrepresentableSamples || !latestMilliseconds.ContainsKey("FullCapture") || !latestMilliseconds.ContainsKey("LightweightCapture")) incomplete.Add("performance");
        var catalog = input.Pipeline.CatalogCache with { BuildMilliseconds = latestMilliseconds.GetValueOrDefault("CatalogBuild") };
        if (catalog.Status != "Ready") incomplete.Add("collectionPipeline");
        return input with
        {
            Runtime = runtime,
            Lifecycle = lifecycle,
            Operations = operations,
            Storage = storage,
            Ui = observedUi ?? input.Ui,
            Pipeline = input.Pipeline with { CatalogCache = catalog },
            Sources = input.Sources.Select(source => source with { CaptureMilliseconds = latestMilliseconds.GetValueOrDefault("Source." + source.SourceKey) }).ToImmutableArray(),
            IncompleteSections = incomplete.ToImmutable(),
        };
    }
}
