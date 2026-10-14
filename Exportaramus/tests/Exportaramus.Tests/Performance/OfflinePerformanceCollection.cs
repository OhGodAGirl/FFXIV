namespace Exportaramus.Tests.Performance;

/// <summary>Runs measured budgets without concurrent fixture work competing for the process.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OfflinePerformanceCollection
{
    public const string Name = "Offline performance measurements";
}
