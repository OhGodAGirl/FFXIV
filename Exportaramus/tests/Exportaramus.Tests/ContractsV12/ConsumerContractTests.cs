// Regression parity with Contracts/ConsumerContractTests.cs, targeting the independently pinned 1.2 assembly.
using ProductCollectTests = Exportaramus.Tests.Contracts.ProductCollectTests;
using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_2;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Tests.Snapshots;

namespace Exportaramus.Tests.ContractsV12;

public sealed class ConsumerContractTests
{
    internal static string Root
    {
        get { var folder = new DirectoryInfo(AppContext.BaseDirectory); while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "project", "Exportaramus.csproj"))) folder = folder.Parent; return folder?.FullName ?? throw new InvalidOperationException(); }
    }
    internal static string Fixture(string name) => V12FixtureAdapter.Adapt(File.ReadAllText(Path.Combine(Root, "tests", "fixtures", "Contracts", "1.0", "fixtures", name.Contains("-invalid-", StringComparison.Ordinal) ? "invalid" : "valid", name)), name);
    internal static string Edit(string payload, Action<JsonObject> edit) { var root = JsonNode.Parse(payload)!.AsObject(); edit(root); return root.ToJsonString(); }

    [Theory]
    [InlineData("compact-valid-full.json")]
    [InlineData("compact-valid-mixed.json")]
    [InlineData("compact-valid-empty-source.json")]
    [InlineData("compact-valid-unknown-property.json")]
    public void AcceptsValidCompactFixtures(string file)
    {
        var result = CollectionDataSemanticValidator.ValidateSnapshot(Fixture(file), "12345678901234567");
        Assert.True(result.Success, result.Code.ToString()); Assert.Equal("1.2", result.Value!.SchemaVersion);
    }

    [Theory]
    [InlineData("compact-invalid-unavailable-with-items.json")]
    [InlineData("compact-invalid-quantity-mode.json")]
    [InlineData("compact-invalid-unsorted-or-duplicate.json")]
    [InlineData("compact-invalid-version-1_1.json")]
    [InlineData("compact-invalid-version-2_0.json")]
    [InlineData("compact-invalid-version-1.json")]
    [InlineData("compact-invalid-version-1__0.json")]
    [InlineData("compact-invalid-version-1e0_0.json")]
    public void RejectsInvalidCompactFixtures(string file) => Assert.False(CollectionDataSemanticValidator.ValidateSnapshot(Fixture(file), "12345678901234567").Success);

    [Theory]
    [InlineData("compendium-valid-complete.json", true)]
    [InlineData("compendium-valid-stale-storage.json", true)]
    [InlineData("compendium-invalid-reported-proves-owned.json", false)]
    [InlineData("compendium-invalid-partial-included-collection.json", false)]
    public void RichFixturesHaveExpectedAcceptance(string file, bool accepted)
    {
        var result = CollectionDataSemanticValidator.ValidateRich(Fixture(file)); Assert.Equal(accepted, result.Success);
    }

    [Fact]
    public void CapabilitiesAndNoticesValidatePinnedHashAndExactContract()
    {
        var caps = Fixture("capabilities-valid.json"); Assert.True(CollectionDataSemanticValidator.ValidateCapabilities(caps).Success);
        Assert.Equal(CollectionDataValidationCode.SchemaHashMismatch, CollectionDataSemanticValidator.ValidateCapabilities(Edit(caps, n => n["collectionDataSchema"]!["sha256"] = new string('0', 64))).Code);
        Assert.False(CollectionDataSemanticValidator.ValidateCapabilities(Edit(caps, n => n["limits"]!["globalReportRate"]!["burst"] = 21)).Success);
        Assert.True(CollectionDataSemanticValidator.ValidateChanged(Fixture("snapshot-changed-valid.json")).Success);
        Assert.True(CollectionDataSemanticValidator.ValidateWithdrawn(Fixture("snapshot-withdrawn-valid.json")).Success);
        Assert.False(CollectionDataSemanticValidator.ValidateChanged(Edit(Fixture("snapshot-changed-valid.json"), n => n["revision"] = 0)).Success);
        Assert.False(CollectionDataSemanticValidator.ValidateChanged(Edit(Fixture("snapshot-changed-valid.json"), n => n["changedDataTypes"] = new JsonArray("ReportedOwnership"))).Success);
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(16384)]
    [InlineData(262144)]
    [InlineData(1048576)]
    [InlineData(4194304)]
    [InlineData(16777216)]
    public void WholeDocumentLimitsAreMeasuredInUtf8Bytes(int limit)
    {
        var exact = "{\"x\":\"" + new string('a', limit - 8) + "\"}";
        var result = CollectionDataSemanticValidator.ParseDocument(exact, limit, false); Assert.True(result.Success); result.Value!.Dispose();
        Assert.Equal(CollectionDataValidationCode.PayloadTooLarge, CollectionDataSemanticValidator.ParseDocument(exact + " ", limit, false).Code);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":\"1.0\",\"nested\":{\"a\":1,\"a\":2}}", CollectionDataValidationCode.InvalidJson)]
    [InlineData("{\"schemaVersion\":\"1.0\",}", CollectionDataValidationCode.InvalidJson)]
    [InlineData("{\"schemaVersion\":/*x*/\"1.0\"}", CollectionDataValidationCode.InvalidJson)]
    [InlineData("{}", CollectionDataValidationCode.MissingSchemaVersion)]
    [InlineData("{\"schemaVersion\":1}", CollectionDataValidationCode.MissingSchemaVersion)]
    [InlineData("{\"schemaVersion\":\"01.0\"}", CollectionDataValidationCode.UnsupportedSchemaVersion)]
    [InlineData("{\"schemaVersion\":\"-1.0\"}", CollectionDataValidationCode.MalformedSchemaVersion)]
    public void StrictParsingHasStableFailureCodes(string payload, CollectionDataValidationCode code) => Assert.Equal(code, CollectionDataSemanticValidator.ParseDocument(payload, 4096, true).Code);

    [Fact]
    public void InvalidSurrogatesDepthAndAdversarialMutationsDoNotEscapeBoundary()
    {
        Assert.Equal(CollectionDataValidationCode.InvalidJson, CollectionDataSemanticValidator.ParseDocument("\ud800", 4096, true).Code);
        Assert.Equal(CollectionDataValidationCode.InvalidJson, CollectionDataSemanticValidator.ParseDocument(new string('[', 65) + new string(']', 65), 4096, true).Code);
        var random = new Random(621); var original = Fixture("compact-valid-full.json");
        for (var i = 0; i < 100; i++)
        {
            var index = random.Next(original.Length); var payload = original[..index] + "\0" + original[index..];
            Assert.False(CollectionDataSemanticValidator.ValidateSnapshot(payload, "12345678901234567").Success);
        }
    }

    [Fact]
    public void RequiredEnumsFormatsMapsQuantitiesAndIdentityAreStrict()
    {
        var payload = Fixture("compact-valid-full.json");
        Assert.Equal(CollectionDataValidationCode.WrongCharacter, CollectionDataSemanticValidator.ValidateSnapshot(payload, "1").Code);
        foreach (var mutate in new Action<JsonObject>[]
        {
            n => n["contentId"] = "18446744073709551616",
            n => n["providerSessionId"] = "no-uuid",
            n => n["capturedAtUtc"] = "2026-99-99T00:00:00Z",
            n => n["sourceHealth"]!["inventory"]!["sourceStatus"] = "Future",
            n => n["sourceHealth"]!["inventory"]!["captureGeneration"] = 2,
            n => n["sourceCapabilities"]!["inventory"]!["supportsQuantity"] = false,
            n => n["currentItemPresence"]!["armoire"]!["items"]![0]!["quantity"] = 1,
            n => n["currentItemPresence"]!["inventory"]!["items"]![0]!["quantity"] = 4294967296L,
            n => n["reportedOwnership"]!["items"]![0]!["reporter"] = " consumer ",
            n => n["reportedOwnership"]!["items"]![0]!["reporter"] = new string('\u00e9', 33),
        }) Assert.False(CollectionDataSemanticValidator.ValidateSnapshot(Edit(payload, mutate), "12345678901234567").Success);
    }

    [Fact]
    public void EveryFfxivCollectFixtureAndActualProjectionPassIndependentValidation()
    {
        var directory = Path.Combine(Root, "tests", "fixtures", "FFXIVCollect", "1.0", "fixtures");
        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
            Assert.Equal(Path.GetFileName(Path.GetDirectoryName(file)) == "valid", CollectionDataSemanticValidator.ValidateFFXIVCollect(File.ReadAllText(file)).Success);
        var output = FFXIVCollectImportSerializer.Serialize(ProductCollectTests.CompleteProjection(FFXIVCollectContract.LoadEmbedded()), FFXIVCollectContract.LoadEmbedded());
        Assert.True(CollectionDataSemanticValidator.ValidateFFXIVCollect(output.Json).Success);
        foreach (var payload in new[] { "{\"bardings\":[1.0]}", "{\"bardings\":[1e0]}", "{\"bardings\":[2,1]}", "{\"cards\":[],\"bardings\":[]}" })
            Assert.False(CollectionDataSemanticValidator.ValidateFFXIVCollect(payload).Success);
    }

    [Fact]
    public void ActualSchemaValidOutputsAcceptExactWholeDocumentBoundaries()
    {
        var cases = new (string Payload, int Limit, Func<string, bool> Validate)[]
        {
            (Fixture("compact-valid-full.json"), 4194304, p => CollectionDataSemanticValidator.ValidateSnapshot(p, "12345678901234567").Success),
            (Fixture("capabilities-valid.json"), 16384, p => CollectionDataSemanticValidator.ValidateCapabilities(p).Success),
            (Fixture("snapshot-changed-valid.json"), 4096, p => CollectionDataSemanticValidator.ValidateChanged(p).Success),
            (Fixture("snapshot-withdrawn-valid.json"), 4096, p => CollectionDataSemanticValidator.ValidateWithdrawn(p).Success),
            (Fixture("compendium-valid-complete.json"), 16777216, p => CollectionDataSemanticValidator.ValidateRich(p).Success),
            ("{\"bardings\":[]}", 1048576, p => CollectionDataSemanticValidator.ValidateFFXIVCollect(p).Success),
        };
        foreach (var (payload, limit, validate) in cases)
        {
            var exact = payload + new string(' ', limit - System.Text.Encoding.UTF8.GetByteCount(payload));
            Assert.True(validate(exact)); Assert.False(validate(exact + " "));
        }
    }

    [Fact]
    public void ArrayReportAndQuantityBoundariesAreWholeSourceDecisions()
    {
        var full = Fixture("compact-valid-full.json");
        var fifty = Edit(full, n => n["unlocks"]!["mounts"]!["unlockedIds"] = new JsonArray(Enumerable.Range(1, 50000).Select(i => (JsonNode?)JsonValue.Create(i)).ToArray()));
        Assert.True(CollectionDataSemanticValidator.ValidateSnapshot(fifty, "12345678901234567").Success);
        var over = Edit(fifty, n => n["unlocks"]!["mounts"]!["unlockedIds"]!.AsArray().Add(50001));
        Assert.False(CollectionDataSemanticValidator.ValidateSnapshot(over, "12345678901234567").Success);
        var quantities = Edit(full, n => n["currentItemPresence"]!["inventory"]!["items"]![0]!["quantity"] = uint.MaxValue);
        Assert.True(CollectionDataSemanticValidator.ValidateSnapshot(quantities, "12345678901234567").Success);
        var reports = Edit(full, n => n["reportedOwnership"]!["items"] = new JsonArray(Enumerable.Range(1, 10000).Select(i => (JsonNode?)new JsonObject { ["itemId"] = i, ["reportedAtUtc"] = "2026-09-06T12:00:00Z", ["reporter"] = "FixtureConsumer" }).ToArray()));
        Assert.True(CollectionDataSemanticValidator.ValidateSnapshot(reports, "12345678901234567").Success);
        Assert.False(CollectionDataSemanticValidator.ValidateSnapshot(Edit(reports, n => n["reportedOwnership"]!["items"]!.AsArray().Add(new JsonObject { ["itemId"] = 10001, ["reportedAtUtc"] = "2026-09-06T12:00:00Z", ["reporter"] = "FixtureConsumer" })), "12345678901234567").Success);
    }

    [Fact]
    public void RichDerivedEvidenceCannotUsePartialOrWrongRelationships()
    {
        var rich = V12FixtureAdapter.Rich(SnapshotFixture.Create(), "Fixture", SnapshotFixture.Now);
        foreach (var edit in new Action<JsonObject>[]
        {
            n => n["collections"]!["outfits"]!["items"]![0]!["id"] = 1,
            n => n["collections"]!["outfits"]!["items"]![0]!["ownershipEvidence"]!["facts"]!.AsArray().RemoveAt(0),
            n => n["collections"]!["relics"]!["items"]![0]!["id"] = 1,
            n => n["collections"]!["mounts"]!["items"]![0]!["owned"] = false,
            n => n["collections"]!["quests"]!["items"]![0]!["status"] = "InProgress",
            n => n["collections"]!["quests"]!["items"]![0]!["description"] = "Static quest text is prohibited.",
            n => n["collections"]!["achievements"]!["items"]![0]!["rewardCollected"] = true,
            n => n["omittedCollections"]!["mounts"] = new JsonObject { ["reasonCode"] = "CaptureFailed", ["message"] = "Must not also be included." },
            n => n["collections"]!["armoires"]!["items"]![0]!["ownershipEvidence"]!["facts"]![0]!["sourceKey"] = "inventory",
        }) Assert.False(CollectionDataSemanticValidator.ValidateRich(Edit(rich, edit)).Success);
    }

    [Fact]
    public void EveryKnownFixtureFieldRejectsAnIncompatibleJsonType()
    {
        var documents = new (string Payload, Func<string, bool> Validate)[]
        {
            (Fixture("compact-valid-full.json"), p => CollectionDataSemanticValidator.ValidateSnapshot(p, "12345678901234567").Success),
            (Fixture("capabilities-valid.json"), p => CollectionDataSemanticValidator.ValidateCapabilities(p).Success),
            (Fixture("snapshot-changed-valid.json"), p => CollectionDataSemanticValidator.ValidateChanged(p).Success),
            (Fixture("snapshot-withdrawn-valid.json"), p => CollectionDataSemanticValidator.ValidateWithdrawn(p).Success),
            (Fixture("compendium-valid-complete.json"), p => CollectionDataSemanticValidator.ValidateRich(p).Success),
        };
        foreach (var (payload, validate) in documents)
        {
            var original = JsonNode.Parse(payload)!;
            foreach (var path in Paths(original, []))
            {
                var copy = original.DeepClone(); var parent = copy;
                foreach (var segment in path[..^1]) parent = parent is JsonArray ? parent[int.Parse(segment, System.Globalization.CultureInfo.InvariantCulture)]! : parent[segment]!;
                var last = path[^1]; var existing = parent is JsonArray ? parent[int.Parse(last, System.Globalization.CultureInfo.InvariantCulture)] : parent[last];
                JsonNode replacement = existing is JsonObject or JsonArray ? JsonValue.Create(false)! : new JsonObject();
                if (parent is JsonArray array) array[int.Parse(last, System.Globalization.CultureInfo.InvariantCulture)] = replacement; else parent[last] = replacement;
                Assert.False(validate(copy.ToJsonString()), string.Join('/', path));
            }
        }

        static IEnumerable<string[]> Paths(JsonNode? node, string[] prefix)
        {
            if (node is JsonObject obj)
                foreach (var property in obj)
                {
                    var path = prefix.Append(property.Key).ToArray(); yield return path;
                    foreach (var descendant in Paths(property.Value, path)) yield return descendant;
                }
            else if (node is JsonArray array)
                for (var i = 0; i < array.Count; i++)
                {
                    var path = prefix.Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(); yield return path;
                    foreach (var descendant in Paths(array[i], path)) yield return descendant;
                }
        }
    }

    [Fact]
    public void SourceGeneratedDtosRoundTripEveryRequiredFieldAndIgnoreOptionalExtensions()
    {
        var snapshot = CollectionDataSemanticValidator.ValidateSnapshot(Fixture("compact-valid-unknown-property.json"), "12345678901234567").Value!;
        var encoded = JsonSerializer.Serialize(snapshot, CollectionDataJsonContext.Default.CollectionDataSnapshot);
        Assert.DoesNotContain("futureOptional", encoded); Assert.True(CollectionDataSemanticValidator.ValidateSnapshot(encoded, snapshot.ContentId).Success);
        var capabilities = CollectionDataSemanticValidator.ValidateCapabilities(Fixture("capabilities-valid.json")).Value!;
        Assert.True(CollectionDataSemanticValidator.ValidateCapabilities(JsonSerializer.Serialize(capabilities, CollectionDataJsonContext.Default.CollectionDataCapabilities)).Success);
        var changed = CollectionDataSemanticValidator.ValidateChanged(Fixture("snapshot-changed-valid.json")).Value!;
        Assert.True(CollectionDataSemanticValidator.ValidateChanged(JsonSerializer.Serialize(changed, CollectionDataJsonContext.Default.CollectionDataChangedNotice)).Success);
        var withdrawn = CollectionDataSemanticValidator.ValidateWithdrawn(Fixture("snapshot-withdrawn-valid.json")).Value!;
        Assert.True(CollectionDataSemanticValidator.ValidateWithdrawn(JsonSerializer.Serialize(withdrawn, CollectionDataJsonContext.Default.CollectionDataWithdrawnNotice)).Success);
    }
}


