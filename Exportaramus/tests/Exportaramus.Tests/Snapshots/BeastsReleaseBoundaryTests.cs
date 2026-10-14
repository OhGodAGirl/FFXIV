using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.Features.CollectionData;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class BeastsReleaseBoundaryTests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "beasts-release-boundaries", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("schemaVersion", "1.1")]
    [InlineData("key", "mounts")]
    [InlineData("upstreamCommit", "unreviewed")]
    [InlineData("importerPath", "other.rb")]
    [InlineData("validatedGameDataVersion", " ")]
    [InlineData("validatedGameDataVersion", null)]
    [InlineData("validatedGameDataVersion", "oversize")]
    [InlineData("ids", "empty")]
    [InlineData("ids", "zero")]
    [InlineData("ids", "duplicate")]
    [InlineData("ids", "unsorted")]
    [InlineData("idsSha256", "incorrect")]
    public void UnreviewedOrMalformedBeastContractCannotEnableExport(string property, string? value)
    {
        var document = ContractDocument();
        if (property == "ids")
            document[property] = value switch
            {
                "empty" => new JsonArray(),
                "zero" => new JsonArray(0, 1),
                "duplicate" => new JsonArray(1, 1),
                _ => new JsonArray(2, 1),
            };
        else document[property] = value == "oversize" ? new string('x', 129) : value;
        Assert.Throws<InvalidDataException>(() => BeastsExportContract.Load(Encoding.UTF8.GetBytes(document.ToJsonString())));
    }

    [Fact]
    public void ReviewedContractRetainsExplicitMappingGate()
    {
        var document = ContractDocument();
        document["mappingValidated"] = false;
        var contract = BeastsExportContract.Load(Encoding.UTF8.GetBytes(document.ToJsonString()));
        Assert.False(contract.SupportsGameVersion("2026.09.01.0000.0000"));
        Assert.Equal<uint>([1, 2], contract.RecognizedIds.Order());
    }

    [Fact]
    public void CurrentCompleteBeastsExportsOnlyObservedUnlockedCanonicalIds()
    {
        var data = SnapshotFixture.Create();
        var beast = data.Catalog.Collections["beasts"] with
        {
            Items = [new CatalogItem(1, 1, "First", false), new CatalogItem(2, 2, "Second", false)],
        };
        data = CollectionDataAssembler.Assemble(data.Catalog with { Collections = data.Catalog.Collections.SetItem("beasts", beast) },
            data.State with { Unlocks = data.State.Unlocks.SetItem("beasts", new([2], SnapshotFixture.Health(DataType.PermanentUnlock))) });
        var contract = new FFXIVCollectContract(data.Catalog.GameDataVersion, ImmutableDictionary<string, ImmutableHashSet<uint>>.Empty)
        {
            Beasts = new(data.Catalog.GameDataVersion, [1, 2], true),
        };
        var result = FFXIVCollectImportSerializer.Serialize(data, contract);
        Assert.True(result.CanCopy);
        Assert.Equal("{\"beasts\":[2]}", result.Json);
        Assert.Equal("beasts", Assert.Single(result.IncludedKeys));
    }

    [Fact]
    public void RestoredVersion12CapabilitiesProjectIntoBothOlderValidatedContracts()
    {
        var canonical = CapabilityDocumentWriter.BuildV12(SnapshotFixture.Create(), false);
        var old = CollectionDataLegacyProjection.Capabilities(canonical);
        var extended = CollectionDataLegacyProjection.CapabilitiesV11(canonical);
        Assert.True(Exportaramus.ConsumerKit.V1.CollectionDataSemanticValidator.ValidateCapabilities(old).Success);
        var result = Exportaramus.ConsumerKit.V1_1.CollectionDataSemanticValidator.ValidateCapabilities(extended);
        Assert.True(result.Success, result.Code.ToString());
        Assert.Contains("facewear", extended);
        Assert.DoesNotContain("beasts", extended);
        Assert.False(result.Value!.ReportedOwnershipEvidence.Available);
    }

    [Fact]
    public void StaleBeastsDoNotMakeOlderSnapshotsMixedOrLeakNewCollectionMetadata()
    {
        var data = SnapshotFixture.Create();
        data = CollectionDataAssembler.Assemble(data.Catalog, data.State with
        {
            Unlocks = data.State.Unlocks.SetItem("beasts", data.State.Unlocks["beasts"] with
            {
                Health = SnapshotFixture.Health(DataType.PermanentUnlock, SourceStatus.Stale) with
                { ReasonCode = ReasonCode.CaptureFailed, Reason = "Retained Beast observation." },
            }),
        });
        foreach (var projected in new[] { CollectionDataLegacyProjection.Data(data), CollectionDataLegacyProjection.DataV11(data) })
        {
            Assert.DoesNotContain("beasts", projected.Collections.Keys);
            Assert.DoesNotContain("beasts", projected.SourceHealth.Keys);
            Assert.DoesNotContain("beasts", projected.State.Unlocks.Keys);
            Assert.Equal("Coherent", CollectionDataSerializer.Consistency(projected));
        }
        Assert.Equal("MixedObservationTimes", CollectionDataSerializer.Consistency(data));
    }

    [Theory]
    [InlineData("facewear", "CollectionData 1.1 validation failed:")]
    [InlineData("beasts", "CollectionData 1.2 validation failed:")]
    public void InvalidNewVersionSourceIdentifiesTheFirstRejectedContract(string key, string prefix)
    {
        var data = SnapshotFixture.Create();
        data = CollectionDataAssembler.Assemble(data.Catalog, data.State with
        {
            Unlocks = data.State.Unlocks.SetItem(key, data.State.Unlocks[key] with { UnlockedIds = [0] }),
        });
        var exception = Assert.Throws<InvalidDataException>(() => new CollectionDataPublisher(new(root), null).Prepare(data, "Test", 1, null, false, false, default));
        Assert.StartsWith(prefix, exception.Message);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void SettingOnlyOlderCapabilitiesClearsPreviouslyPublishedVersion12Capabilities()
    {
        var store = new CollectionDataPublicationStore();
        store.SetCapabilities("old", "extended", "newest");
        store.SetCapabilities("replacement", "extended replacement");
        Assert.Equal("replacement", store.GetCapabilities());
        Assert.Equal("extended replacement", store.GetCapabilitiesV11());
        Assert.Empty(store.GetCapabilitiesV12());
    }

    [Fact]
    public void Version12RestoreKeepsDetailedDresserEntriesAndRejectsWrongIdentityClearly()
    {
        var data = SnapshotFixture.Create();
        var dresser = data.State.CurrentItemPresence["glamourDresser"] with
        {
            Items = [new(100, null)],
            DresserEntries = [new(0, 100, 100, null, [new(0, 100)])],
        };
        data = CollectionDataAssembler.Assemble(data.Catalog, data.State with
        { CurrentItemPresence = data.State.CurrentItemPresence.SetItem("glamourDresser", dresser) });
        var json = CollectionDataSerializer.CompactV12(data, 1);
        var restored = CollectionStateReader.Read(json, 123, Guid.NewGuid(), SnapshotFixture.Now.AddMinutes(1));
        var entry = Assert.Single(restored.CurrentItemPresence["glamourDresser"].DresserEntries!.Value);
        Assert.Equal(0, entry.EntryIndex);
        Assert.Equal(100u, entry.RawItemId);
        Assert.Equal(100u, entry.BaseItemId);
        Assert.Null(entry.OutfitId);
        var component = Assert.Single(entry.Components);
        Assert.Equal(0, component.SlotIndex);
        Assert.Equal(100u, component.ItemId);
        Assert.Equal("Stored collection state is invalid.", Assert.Throws<InvalidDataException>(() =>
            CollectionStateReader.Read(json, 456, Guid.NewGuid(), SnapshotFixture.Now)).Message);
    }

    [Fact]
    public void IneligibleBeastExportNamesTheOmittedCollection()
    {
        var data = SnapshotFixture.Create();
        var contract = new FFXIVCollectContract(data.Catalog.GameDataVersion,
            data.Catalog.Collections.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Items.Select(item => item.Id).ToImmutableHashSet()))
        { Beasts = new(data.Catalog.GameDataVersion, [1], false) };
        var export = FFXIVCollectImportSerializer.Serialize(data, contract);
        Assert.True(export.CanCopy);
        Assert.Contains("beasts", export.OmittedKeys);
        Assert.DoesNotContain("beasts", export.IncludedKeys);
        Assert.DoesNotContain("beasts", export.Json!);
    }

    [Fact]
    public async Task WriteDiagnosticIncludesVersion12SidecarUtf8Bytes()
    {
        var storage = new AtomicSnapshotStorage(root);
        const string compact = "{}", rich = "{\"name\":\"é\"}", reports = "{}", sidecar = "{\"schemaVersion\":\"1.2\"}";
        await storage.CommitAsync(123, compact, rich, reports, false, false, SnapshotFixture.Now, default, sidecar);
        Assert.Equal(Encoding.UTF8.GetByteCount(compact + rich + reports + sidecar), storage.Diagnostic.LastWrite.ByteCount);
    }

    [Fact]
    public void Version12CompactOversizeCannotPassThroughLegacyValidation()
    {
        var data = SnapshotFixture.Create();
        data = CollectionDataAssembler.Assemble(data.Catalog, data.State with
        {
            CaptureType = CaptureType.Lightweight,
            Unlocks = data.State.Unlocks.SetItem("beasts", data.State.Unlocks["beasts"] with
            {
                Health = data.State.Unlocks["beasts"].Health with { Reason = new string('x', 4_194_305) },
            }),
        });
        Assert.True(Exportaramus.ConsumerKit.V1_1.CollectionDataSemanticValidator.ValidateSnapshot(CollectionDataSerializer.CompactV11(data, 1), "123").Success);
        Assert.Throws<OutputSizeLimitException>(() => new CollectionDataPublisher(new(root), null).Prepare(data, "Test", 1, null, false, false, default));
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Version12RichInvalidOrOversizeBeastCannotReachPersistence(bool oversize)
    {
        var data = SnapshotFixture.Create();
        var beast = data.Catalog.Collections["beasts"] with
        {
            Items = [new CatalogItem(1, 1, oversize ? new string('x', 16_777_217) : null!, false)],
        };
        data = CollectionDataAssembler.Assemble(data.Catalog with { Collections = data.Catalog.Collections.SetItem("beasts", beast) }, data.State);
        Assert.True(Exportaramus.ConsumerKit.V1.CollectionDataSemanticValidator.ValidateRich(CollectionDataSerializer.Rich(data, "Test", SnapshotFixture.Now)).Success);
        var publisher = new CollectionDataPublisher(new(root), null);
        if (oversize) Assert.Throws<OutputSizeLimitException>(() => publisher.Prepare(data, "Test", 1, null, false, false, default));
        else Assert.StartsWith("Rich snapshot 1.2 validation failed:", Assert.Throws<InvalidDataException>(() => publisher.Prepare(data, "Test", 1, null, false, false, default)).Message);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task RecoveryRecognizesVersionedRichDestinationAndCleansCommittedTemporaryFiles()
    {
        const string destination = "current-snapshot-v1.2.json";
        var temporary = destination + "." + new string('a', 32) + ".tmp";
        var backup = temporary + ".bak";
        Directory.CreateDirectory(Path.Combine(root, "state"));
        await File.WriteAllTextAsync(Path.Combine(root, destination), "{\"accepted\":true}");
        await File.WriteAllTextAsync(Path.Combine(root, temporary), "temporary");
        await File.WriteAllTextAsync(Path.Combine(root, backup), "old");
        var journal = Path.Combine(root, "state", "write-recovery.json");
        await File.WriteAllTextAsync(journal, JsonSerializer.Serialize(new
        {
            format = "exportaramus-write-recovery", version = 1, committed = true,
            files = new[] { new { destination, temporary, backup, existed = true, delete = false, applied = true } },
        }));
        var storage = new AtomicSnapshotStorage(root);
        Assert.Null(await storage.ReadAsync(123, false, default));
        Assert.Equal("Recovered", storage.RecoveryState);
        Assert.Equal("{\"accepted\":true}", await File.ReadAllTextAsync(storage.RichV12Path));
        Assert.False(File.Exists(Path.Combine(root, temporary)));
        Assert.False(File.Exists(Path.Combine(root, backup)));
        Assert.False(File.Exists(journal));
    }

    private static JsonObject ContractDocument() => new()
    {
        ["schemaVersion"] = "1.2", ["key"] = "beasts",
        ["upstreamCommit"] = "30d92ee4ea0bbbd678fff5c4279a41f7724b357d",
        ["importerPath"] = "app/controllers/import_controller.rb",
        ["validatedGameDataVersion"] = "2026.09.01.0000.0000",
        ["ids"] = new JsonArray(1, 2),
        ["idsSha256"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("1,2"))),
        ["mappingValidated"] = true,
    };

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
