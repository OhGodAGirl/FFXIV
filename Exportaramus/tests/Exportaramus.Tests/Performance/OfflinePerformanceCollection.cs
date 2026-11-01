namespace Exportaramus.Tests.Performance;

/// <summary>Runs performance tests without other fixtures running at the same time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OfflinePerformanceCollection
{
    public const string Name = "Offline performance measurements";
}
