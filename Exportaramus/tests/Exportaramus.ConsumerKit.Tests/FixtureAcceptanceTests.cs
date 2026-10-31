using System.Text.Json;
using Exportaramus.ConsumerKit.V1_2;

namespace Exportaramus.ConsumerKit.Tests;

public sealed class FixtureAcceptanceTests
{
    public static IEnumerable<object[]> Fixtures()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures");
        foreach (var category in new[] { "valid", "invalid" })
        {
            var paths = Directory.GetFiles(Path.Combine(root, category), "*.json").Order(StringComparer.Ordinal).ToArray();
            if (paths.Length == 0)
                throw new InvalidOperationException($"The shipped {category} fixtures were not copied into the test output.");
            foreach (var path in paths)
                yield return [category, Path.GetFileName(path), category == "valid"];
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EveryShippedFixtureHasItsDeclaredAcceptance(string category, string file, bool expected)
    {
        var payload = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", category, file));
        using var document = JsonDocument.Parse(payload);
        var isExpandedSnapshot = document.RootElement.TryGetProperty("format", out var format) &&
            format.ValueKind == JsonValueKind.String && format.GetString() == "exportaramus-snapshot";
        bool actual;
        if (file.StartsWith("ffxiv-collect-", StringComparison.Ordinal))
            actual = CollectionDataSemanticValidator.ValidateFFXIVCollect(payload).Success;
        else if (file.StartsWith("capabilities-", StringComparison.Ordinal))
            actual = CollectionDataSemanticValidator.ValidateCapabilities(payload).Success;
        else if (file.StartsWith("snapshot-changed-", StringComparison.Ordinal))
            actual = CollectionDataSemanticValidator.ValidateChanged(payload).Success;
        else if (file.StartsWith("snapshot-withdrawn-", StringComparison.Ordinal))
            actual = CollectionDataSemanticValidator.ValidateWithdrawn(payload).Success;
        else if (file.StartsWith("expanded-", StringComparison.Ordinal) || isExpandedSnapshot)
            actual = CollectionDataSemanticValidator.ValidateRich(payload).Success;
        else if (file.StartsWith("compact-", StringComparison.Ordinal))
            actual = CollectionDataSemanticValidator.ValidateSnapshot(payload, ContractFixture.Character).Success;
        else
            throw new InvalidOperationException($"No validator is assigned to fixture {file}.");

        Assert.Equal(expected, actual);
    }
}
