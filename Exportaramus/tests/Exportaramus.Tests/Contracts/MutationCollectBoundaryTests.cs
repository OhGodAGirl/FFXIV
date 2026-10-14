using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Exportaramus.Features.CollectionData;
using Exportaramus.Features.CollectionData.Contracts;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Tests.Contracts;

public sealed class MutationCollectBoundaryTests
{
    [Theory]
    [InlineData(1048576, true)]
    [InlineData(1048577, false)]
    public void ActualExportAcceptsExactlyOneMiBAndRejectsOneAdditionalByte(int byteCount, bool accepted)
    {
        var keys = CollectionKeys.Collect.ToArray();
        var ids = keys.ToDictionary(key => key, _ => new List<uint> { 1000000000 });
        ids[keys[^1]][0] = 100000000;
        var initialSize = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(ids));
        var additions = (1048576 - initialSize + 10) / 11;
        var surplus = initialSize + additions * 11 - 1048576;
        // Distinct ten-digit IDs add eleven bytes each. Two single-ID arrays absorb
        // the remainder, leaving the final nine-digit ID for the one-byte boundary.
        for (var index = 0; index < additions; index++)
            ids[keys[index / 49999]].Add(1000000001u + (uint)(index % 49999));
        ids[keys[^2]][0] = PowerOfTen(9 - Math.Min(9, surplus));
        if (surplus > 9) ids[keys[^3]][0] = PowerOfTen(9 - (surplus - 9));
        if (byteCount == 1048577) ids[keys[^1]][0] = 1000000000;
        var expected = JsonSerializer.Serialize(ids);
        Assert.Equal(byteCount, Encoding.UTF8.GetByteCount(expected));
        Assert.All(ids.Values, values => { Assert.InRange(values.Count, 1, 50000); Assert.Equal(values.Count, values.Distinct().Count()); });

        // The injectable contract models a valid future catalog without changing pinned artifacts.
        var contract = FFXIVCollectContract.LoadEmbedded() with
        {
            // This byte-boundary fixture deliberately models the original fifteen-key contract.
            Beasts = null,
            RecognizedIds = ids.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.ToImmutableHashSet())
        };
        var data = ProductCollectTests.CompleteProjection(contract);
        data = data with { Collections = data.Collections.ToImmutableDictionary(pair => pair.Key, pair => pair.Value with { Items = [.. pair.Value.Items.Select(item => item with { Owned = true })] }) };
        var output = FFXIVCollectImportSerializer.Serialize(data, contract);
        Assert.Equal(accepted, output.CanCopy);
        if (accepted)
        {
            Assert.Null(output.Error);
            Assert.Empty(output.OmittedKeys);
            Assert.Equal(keys, output.IncludedKeys.ToArray());
            Assert.Equal(expected, output.Json);
            Assert.Equal(1048576, Encoding.UTF8.GetByteCount(output.Json!));
        }
        else
        {
            Assert.Null(output.Json);
            Assert.Empty(output.IncludedKeys);
            Assert.Equal(keys, output.OmittedKeys.ToArray());
            Assert.False(string.IsNullOrWhiteSpace(output.Error));
            Assert.Contains("size limit", output.Error);
        }
    }

    [Theory]
    [InlineData("missing-contract")]
    [InlineData("incompatible-version")]
    [InlineData("no-complete-collections")]
    public void UnavailableExportsExplainWhyCopyIsDisabled(string reason)
    {
        FFXIVCollectContract? contract = FFXIVCollectContract.LoadEmbedded();
        var data = ProductCollectTests.CompleteProjection(contract);
        if (reason == "missing-contract") contract = null;
        if (reason == "incompatible-version") contract = contract! with { GameDataVersion = "unrecognized" };
        if (reason == "no-complete-collections") data = data with { Collections = ImmutableDictionary<string, CollectionResult>.Empty };
        var output = FFXIVCollectImportSerializer.Serialize(data, contract);
        Assert.False(output.CanCopy);
        Assert.Null(output.Json);
        Assert.Empty(output.IncludedKeys);
        Assert.Equal(CollectionKeys.Collect.ToArray(), output.OmittedKeys.ToArray());
        Assert.False(string.IsNullOrWhiteSpace(output.Error));
    }

    private static uint PowerOfTen(int exponent)
    {
        uint result = 1;
        for (var index = 0; index < exponent; index++) result *= 10;
        return result;
    }
}
