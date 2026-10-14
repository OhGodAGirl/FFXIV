using System.Collections.Immutable;
using System.Text.Json;
using Exportaramus.Features.CollectionData;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.Contracts;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Tests.Snapshots;

namespace Exportaramus.Tests.Contracts;

public sealed class ProductCollectTests
{
    internal static CollectionData CompleteProjection(FFXIVCollectContract contract)
    {
        var fixture = SnapshotFixture.Create(); var definitions = fixture.Catalog.Collections.ToBuilder(); var projections = fixture.Collections.ToBuilder();
        foreach (var key in CollectionKeys.Collect)
        {
            var ids = contract.RecognizedIds[key].Order().ToArray(); var definition = definitions[key]; var collection = projections[key];
            definitions[key] = definition with { Items = [.. ids.Select(id => new CatalogItem(id, key == "armoires" ? id - 1 : id, "Fixture", false))] };
            projections[key] = collection with { Items = [.. ids.Select(id => new CollectionItem(id, "Fixture", id == ids[0], collection.Items[0].OwnershipEvidence))] };
        }
        // This fixture supplies accepted projection values to the stateless serializer. Derivation has separate tests.
        return fixture with { Catalog = fixture.Catalog with { GameDataVersion = contract.GameDataVersion, Collections = definitions.ToImmutable() }, Collections = projections.ToImmutable() };
    }

    [Fact]
    public void EmbeddedContractMatchesEveryPinnedCatalogAndAllFifteenPreparedKeys()
    {
        var contract = FFXIVCollectContract.LoadEmbedded(); var data = CompleteProjection(contract);
        var result = FFXIVCollectImportSerializer.Serialize(data, contract);
        Assert.True(result.CanCopy); Assert.Null(result.Error); Assert.Equal<string>(["beasts"], result.OmittedKeys); Assert.Equal(CollectionKeys.Collect.ToArray(), result.IncludedKeys.ToArray());
        // The complete catalog fixture proves the fifteen original keys; its Beasts domain is deliberately incomplete.
        Assert.NotNull(contract.Beasts);
        Assert.True(contract.Beasts.SupportsGameVersion(data.Catalog.GameDataVersion));
        using var document = JsonDocument.Parse(result.Json!);
        Assert.Equal(CollectionKeys.Collect, document.RootElement.EnumerateObject().Select(p => p.Name));
        foreach (var key in CollectionKeys.Collect) Assert.Equal(contract.RecognizedIds[key].Min(), Assert.Single(document.RootElement.GetProperty(key).EnumerateArray()).GetUInt32());
        Assert.True(Exportaramus.ConsumerKit.V1.CollectionDataSemanticValidator.ValidateFFXIVCollect(result.Json).Success);
    }

    [Fact]
    public void LaterEmptyProjectionClearsAndNeverMergesPreviousOwnedIds()
    {
        var contract = FFXIVCollectContract.LoadEmbedded(); var data = CompleteProjection(contract); var before = FFXIVCollectImportSerializer.Serialize(data, contract);
        var empty = data with { Collections = data.Collections.ToImmutableDictionary(p => p.Key, p => p.Value with { Items = [.. p.Value.Items.Select(i => i with { Owned = false })] }) };
        var after = FFXIVCollectImportSerializer.Serialize(empty, contract);
        Assert.True(before.CanCopy); Assert.True(after.CanCopy); Assert.NotEqual(before.Json, after.Json);
        using var document = JsonDocument.Parse(after.Json!); Assert.All(document.RootElement.EnumerateObject(), p => Assert.Equal(0, p.Value.GetArrayLength()));
        Assert.Equal(before.Json, FFXIVCollectImportSerializer.Serialize(data, contract).Json);
    }

    [Fact]
    public void MissingPartialUnvalidatedAndUnmappedCollectionsAreOmittedAsWholeKeys()
    {
        var contract = FFXIVCollectContract.LoadEmbedded(); var data = CompleteProjection(contract);
        var collections = data.Collections.Remove("bardings").SetItem("cards", data.Collections["cards"] with { Health = data.Collections["cards"].Health with { Completeness = Completeness.Partial } })
            .SetItem("frames", data.Collections["frames"] with { Health = data.Collections["frames"].Health with { CapabilityState = CapabilityState.UnavailablePendingValidation } });
        var definitions = data.Catalog.Collections.SetItem("hairstyles", data.Catalog.Collections["hairstyles"] with { Items = [] });
        var next = FFXIVCollectImportSerializer.Serialize(data with { Collections = collections, Catalog = data.Catalog with { Collections = definitions } }, contract with { RecognizedIds = contract.RecognizedIds.Remove("leves") });
        Assert.True(next.CanCopy);
        foreach (var key in new[] { "bardings", "cards", "frames", "hairstyles", "leves" }) Assert.Contains(key, next.OmittedKeys);
    }

    [Fact]
    public void StaleInputsAreEligibleOnlyForArmoireAndDresserCarryForward()
    {
        var contract = FFXIVCollectContract.LoadEmbedded(); var data = CompleteProjection(contract); var armoires = data.Collections["armoires"];
        var allowed = armoires with { Health = armoires.Health with { SourceStatus = SourceStatus.Stale }, Items = [.. armoires.Items.Select(i => i with { OwnershipEvidence = i.OwnershipEvidence with { Facts = [new(DataType.CurrentItemPresence, "armoire", 100, "Present", SourceStatus.Stale)] } })] };
        var accepted = FFXIVCollectImportSerializer.Serialize(data with { Collections = data.Collections.SetItem("armoires", allowed) }, contract); Assert.Contains("armoires", accepted.IncludedKeys);
        var dresser = allowed with { Items = [.. allowed.Items.Select(i => i with { OwnershipEvidence = i.OwnershipEvidence with { Facts = [new(DataType.CurrentItemPresence, "glamourDresser", 100, "Present", SourceStatus.Stale)] } })] };
        Assert.Contains("armoires", FFXIVCollectImportSerializer.Serialize(data with { Collections = data.Collections.SetItem("armoires", dresser) }, contract).IncludedKeys);
        var denied = allowed with { Items = [.. allowed.Items.Select(i => i with { OwnershipEvidence = i.OwnershipEvidence with { Facts = [new(DataType.CurrentItemPresence, "inventory", 100, "Present", SourceStatus.Stale)] } })] };
        var rejected = FFXIVCollectImportSerializer.Serialize(data with { Collections = data.Collections.SetItem("armoires", denied) }, contract); Assert.Contains("armoires", rejected.OmittedKeys);
    }

    [Fact]
    public void MissingIncompatibleAndOversizedOutputsDisableCopy()
    {
        var contract = FFXIVCollectContract.LoadEmbedded(); var data = CompleteProjection(contract);
        Assert.False(FFXIVCollectImportSerializer.Serialize(data, null).CanCopy);
        Assert.False(FFXIVCollectImportSerializer.Serialize(data, contract with { GameDataVersion = "unrecognized" }).CanCopy);
        Assert.False(FFXIVCollectImportSerializer.Serialize(data with { Collections = ImmutableDictionary<string, CollectionResult>.Empty }, contract).CanCopy);
        var ids = Enumerable.Range(1, 50000).Select(i => 4000000000u + (uint)i).ToImmutableHashSet();
        var largeContract = contract with { RecognizedIds = CollectionKeys.Collect.ToImmutableDictionary(k => k, _ => ids) };
        var large = CompleteProjection(largeContract);
        large = large with { Collections = large.Collections.ToImmutableDictionary(p => p.Key, p => p.Value with { Items = [.. p.Value.Items.Select(i => i with { Owned = true })] }) };
        var result = FFXIVCollectImportSerializer.Serialize(large, largeContract); Assert.False(result.CanCopy); Assert.Contains("size limit", result.Error);
    }

    [Fact]
    public void MappingUsesPositiveCabinetProjectionIdAndRemovesUnknownDuplicateUnownedIds()
    {
        var data = SnapshotFixture.Create(); var item = data.Collections["armoires"].Items[0];
        var collection = data.Collections["armoires"] with { Items = [item with { Id = 3 }, item with { Id = 1 }, item with { Id = 3 }, item with { Id = 2, Owned = false }, item with { Id = 99 }] };
        Assert.Equal(new uint[] { 1, 3 }, FFXIVCollectIdMapper.Map(collection, [1, 2, 3]));
        Assert.True(FFXIVCollectIdMapper.Covers(new("armoires", "Armoire", DataType.CurrentItemPresence, true, [new(1, 0, "First Cabinet Row", false)]), [1]));
    }
}

