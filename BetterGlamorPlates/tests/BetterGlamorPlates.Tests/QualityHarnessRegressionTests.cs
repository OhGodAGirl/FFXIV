namespace BetterGlamorPlates.Tests;

/// <summary>Locks down confirmed false-green fixes in the plugin-local quality harness.</summary>
public sealed class QualityHarnessRegressionTests
{
    private static readonly string Root = FindPluginRoot();

    /// <summary>Verifies mutation-only test assemblies cannot dilute shipped coverage.</summary>
    [Fact]
    public void CoverageEnumerationExcludesTheMutationHarness()
    {
        var source = File.ReadAllText(Path.Combine(Root, "tests", "run-quality.ps1"));
        if (source.Contains("./tests/test-repository.ps1", StringComparison.Ordinal))
        {
            Assert.Contains("& ./tests/test-repository.ps1 -Configuration $Configuration", source, StringComparison.Ordinal);
            source = File.ReadAllText(Path.Combine(Root, "tests", "test-repository.ps1"));
            Assert.Contains("foreach ($testProject in $testProjects)", source, StringComparison.Ordinal);
            Assert.Contains("--collect:XPlat Code Coverage", source, StringComparison.Ordinal);
        }

        Assert.Contains(
            "Where-Object { $_.Name -notlike '*.Mutation.Tests.csproj' }",
            source,
            StringComparison.Ordinal);
    }

    /// <summary>Verifies strict mutation requires the loaded probe and full target inventory.</summary>
    [Fact]
    public void MutationRunnerRejectsFalseGreenInstrumentationAndPartialTargets()
    {
        var source = File.ReadAllText(Path.Combine(Root, "tests", "test-mutation.ps1"));

        Assert.Contains("Mutation instrumentation proof failed", source, StringComparison.Ordinal);
        Assert.Contains(
            "Mutation targets must exactly match the complete deterministic coverage inventory",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "test-mutation-report.ps1",
            source,
            StringComparison.Ordinal);
    }

    /// <summary>Quality owns no authority to stop another plugin's build servers.</summary>
    [Fact]
    public void QualityDoesNotStopSharedBuildServers()
    {
        var source = File.ReadAllText(Path.Combine(Root, "tests", "run-quality.ps1"));
        Assert.DoesNotContain("build-server shutdown", source, StringComparison.Ordinal);
        Assert.Contains("test-smoke.ps1", source, StringComparison.Ordinal);
        Assert.Contains("test-mutation.ps1", source, StringComparison.Ordinal);
    }

    private static string FindPluginRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props"))
                && Directory.Exists(Path.Combine(current.FullName, "src")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the plugin root.");
    }
}
