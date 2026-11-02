using System.Security.Cryptography;
using System.Text.Json;

namespace CollectionShopper.Tests;

public sealed class ComponentTestInventoryTests
{
    [Fact]
    public void EveryOwnedSourceHasAnExplicitTestOrReviewedBoundaryEntry()
    {
        // Arrange
        var root = FindRoot();
        using var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests/component-test-inventory.json")));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests/coverage-manifest.json")));
        var covered = manifest.RootElement.GetProperty("includedProductionFiles").EnumerateArray()
            .Select(value => value.GetString()!).ToHashSet(StringComparer.Ordinal);
        var sourceFiles = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path).Replace('\\', '/').StartsWith("src/Contracts/", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).Order().ToArray();
        // Act
        var entries = inventory.RootElement.GetProperty("components").EnumerateArray().ToArray();
        // Assert
        Assert.Equal(sourceFiles, entries.Select(entry => entry.GetProperty("path").GetString()).Order().ToArray());
        foreach (var entry in entries)
        {
            var path = entry.GetProperty("path").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("evidence").GetString()), path);
            var tests = entry.GetProperty("tests").EnumerateArray().Select(value => value.GetString()!).ToArray();
            Assert.NotEmpty(tests);
            Assert.All(tests, test => Assert.True(File.Exists(Path.Combine(root, test)), $"Missing test evidence: {test}"));
            Assert.Equal(covered.Contains(path), entry.GetProperty("coverageGate").GetBoolean());
            if (!covered.Contains(path))
            {
                var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, path))));
                Assert.Equal(entry.GetProperty("reviewedSourceSha256").GetString(), actualHash);
            }
            if (entry.GetProperty("kind").GetString() == "native-adapter")
                Assert.NotEmpty(entry.GetProperty("liveChecks").EnumerateArray());
        }
    }

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "CollectionShopper.csproj")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Plugin root not found.");
    }
}
