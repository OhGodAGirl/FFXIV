using System.Security.Cryptography;
using System.Text.Json;

namespace BetterGlamorPlates.Tests;

/// <summary>Detects changes to the retained files from the pinned dependency.</summary>
public sealed class ConsumerKitIntegrityTests
{
    /// <summary>Verifies the retained mapping, original inventory and exact bytes.</summary>
    [Fact]
    public void PinnedConsumerKitMatchesItsChecksumInventory()
    {
        // Arrange
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "BetterGlamorPlates.csproj")))
            root = root.Parent ?? throw new DirectoryNotFoundException();
        var provenanceRoot = Path.Combine(root.FullName, "project", "Contracts", "1.2");
        var manifest = Path.Combine(provenanceRoot, "SHA256SUMS.original");
        using var provenance = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "tests", "release-dependencies.json")));
        var originals = File.ReadAllLines(manifest).Select(entry => entry.Split("  ", 2, StringSplitOptions.None))
            .ToDictionary(parts => parts[1], parts => parts[0], StringComparer.Ordinal);
        using var mappings = JsonDocument.Parse(File.ReadAllText(Path.Combine(provenanceRoot, "retained-files.json")));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var retained = new HashSet<string>(StringComparer.Ordinal);

        // Act and assert: every retained input must match the pinned original.
        Assert.Equal(provenance.RootElement.GetProperty("kitManifestSha256").GetString(),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifest))), ignoreCase: true);
        Assert.Equal(provenance.RootElement.GetProperty("kitFileCount").GetInt32(), originals.Count);
        foreach (var entry in mappings.RootElement.EnumerateArray())
        {
            var original = entry.GetProperty("originalPath").GetString()!;
            var relative = entry.GetProperty("path").GetString()!;
            Assert.True(names.Add(relative), $"Duplicate retained path: {relative}");
            Assert.True(retained.Add(original), $"Duplicate provider entry: {original}");
            Assert.Equal(originals[original], entry.GetProperty("sha256").GetString(), ignoreCase: true);
            var path = Path.GetFullPath(Path.Combine(root.FullName, relative));
            Assert.StartsWith(root.FullName + Path.DirectorySeparatorChar, path, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(path), $"Missing pinned file: {relative}");
            Assert.Equal(originals[original], Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), ignoreCase: true);
        }

        var expected = originals.Keys.Where(path =>
            path.StartsWith("src/Contracts/1.2/", StringComparison.Ordinal) ||
            path.StartsWith("data/Contracts/1.2/", StringComparison.Ordinal) ||
            path.StartsWith("tests/Exportaramus.ConsumerKit.Tests/fixtures/", StringComparison.Ordinal));
        Assert.Equal(expected.Order(), retained.Order());
    }
}
