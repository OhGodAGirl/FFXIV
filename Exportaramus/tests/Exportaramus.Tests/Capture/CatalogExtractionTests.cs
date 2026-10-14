using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Tests.Capture;

public sealed class CatalogExtractionTests
{
    [Fact]
    public void EligibilityUsesEveryRequiredFieldAndPreservesPublishedIds()
    {
        var good = new CatalogRowFact("mounts", 7, "Name", LinkId: 12, ItemId: 99, Order: 1, Icon: 2, IsAetheryte: true, LinkedRowExists: true);
        foreach (var key in new[] { "@items", "mounts", "minions", "emotes", "spells", "aetherytes", "hairstyles", "field_records", "armoires", "frames", "bardings" })
        {
            var row = good with { Key = key };
            var actual = CatalogRowPolicy.Project(row);
            Assert.Equal(key, actual.Key);
            Assert.Equal(key is "frames" or "hairstyles" or "field_records" ? 12u : key == "armoires" ? 8u : 7u, actual.Item.Id);
            Assert.Equal(key == "frames" ? 12u : 7u, actual.Item.SourceRowId);
            Assert.Equal(key == "armoires" ? 99u : (uint?)null, actual.Item.ItemId);
            Assert.Equal("Name", actual.Item.Name); Assert.False(actual.Item.IsFallbackName);
            var bad = key switch
            {
                "hairstyles" or "field_records" or "frames" => row with { LinkId = 0 },
                "armoires" => row with { Order = 0 },
                _ => row with { RowId = 0 },
            };
            Assert.Equal(0u, CatalogRowPolicy.Project(bad).Item.Id);
        }
        foreach (var bad in new[]
        {
            good with { Order = -1 }, good with { Icon = 0 }, good with { Name = "" },
            good with { Key = "minions", Icon = 0 }, good with { Key = "minions", Name = null },
            good with { Key = "emotes", LinkId = 0 }, good with { Key = "emotes", Name = "" },
            good with { Key = "spells", LinkId = 0 }, good with { Key = "aetherytes", IsAetheryte = false },
            good with { Key = "aetherytes", Invisible = true }, good with { Key = "field_records", Name = "" },
            good with { Key = "armoires", ItemId = 0 }, good with { Key = "bardings", Name = "" },
            good with { Key = "npcs", RowId = 2293759 }, good with { Key = "npcs", RowId = 2359296 },
            good with { Key = "npcs", RowId = 2293760, LinkedRowExists = false },
        }) Assert.Equal(0u, CatalogRowPolicy.Project(bad).Item.Id);
        Assert.Equal(7u, CatalogRowPolicy.Project(good with { Order = 0 }).Item.Id);
        Assert.Equal(2293760u, CatalogRowPolicy.Project(good with { Key = "npcs", RowId = 2293760 }).Item.Id);
        Assert.Equal(2359295u, CatalogRowPolicy.Project(good with { Key = "npcs", RowId = 2359295 }).Item.Id);
        Assert.Equal(0u, CatalogRowPolicy.Project(good with { Key = "frames", LinkId = 1 }).Item.Id);
        Assert.Equal(12u, CatalogRowPolicy.Project(good with { Key = "frames" }).Item.SourceRowId);
        Assert.Equal(12u, CatalogRowPolicy.Project(good with { Key = "field_records" }).Item.Id);
        Assert.Equal(7u, CatalogRowPolicy.Project(good with { Key = "hairstyles" }).Item.SourceRowId);
        var cabinet = CatalogRowPolicy.Project(good with { Key = "armoires", RowId = 0 }).Item;
        Assert.Equal(1u, cabinet.Id); Assert.Equal(0u, cabinet.SourceRowId); Assert.Equal(99u, cabinet.ItemId);
        Assert.Throws<OverflowException>(() => CatalogRowPolicy.Project(good with { Key = "armoires", RowId = uint.MaxValue }));
    }

    [Fact]
    public void CatalogNamesRespectFallbackAndQuestMetadataOmission()
    {
        var quest = CatalogRowPolicy.Project(new("quests", 8, "Do not export static text")).Item;
        Assert.Equal("Entry 8", quest.Name); Assert.True(quest.IsFallbackName);
        var hair = CatalogRowPolicy.Project(new("hairstyles", 9, null, LinkId: 17)).Item;
        Assert.Equal("Hairstyle 17", hair.Name); Assert.True(hair.IsFallbackName);
        Assert.False(CatalogRowPolicy.Project(new("hairstyles", 9, "Hint", LinkId: 17, ItemId: 1)).Item.IsFallbackName);
        Assert.True(CatalogRowPolicy.Project(new("frames", 2, " ", LinkId: 573)).Item.IsFallbackName);
        Assert.Equal("Resolved", CatalogRowPolicy.WithItemName(quest, "Resolved").Name);
        Assert.False(CatalogRowPolicy.WithItemName(quest, "Resolved").IsFallbackName);
        Assert.Equal(25, CatalogRowPolicy.Definitions.Length);
        Assert.Equal(21, CatalogRowPolicy.Definitions.Count(d => d.OwnershipKind == DataType.PermanentUnlock));
        Assert.False(CatalogRowPolicy.Definitions.Single(d => d.Key == "glamour_dresser").TotalKnown);
        Assert.All(CatalogRowPolicy.Definitions.Where(d => d.Key != "glamour_dresser"), d => Assert.True(d.TotalKnown));
    }

    [Fact]
    public void ProjectionCountsSkippedRowsAndBoundsBothWorkAndElapsedTime()
    {
        var visited = 0;
        IEnumerable<(string, CatalogItem)> Rows()
        {
            for (var i = 0; i < 1000; i++) { visited++; yield return ("", new(0, 0, "", false)); }
            yield return ("@items", new(99, 99, "Known", false));
            yield return ("mounts", new(3, 3, "Mount", false));
        }
        using var builder = new BoundedCatalogProjection("English", "build", Rows().GetEnumerator(), () => ("English", "build"), () => 0);
        Assert.False(builder.Advance(2)); Assert.Equal(2, visited);
        var completed = false;
        for (var slice = 0; slice < 5 && !completed; slice++) completed = builder.Advance();
        Assert.True(completed, "The remaining 1,000 rows must complete within five 256-row slices when no elapsed-time limit is reached.");
        Assert.Equal(1000, visited); Assert.Equal(99u, Assert.Single(builder.Projection!.KnownItemIds));
        Assert.Equal(new[] { "glamour_dresser", "mounts" }, builder.Projection.Rows.Select(row => row.Key)); Assert.True(builder.Advance());
        var tick = 0.0;
        using var timed = new BoundedCatalogProjection("English", "build", Rows().GetEnumerator(), () => ("English", "build"), () => tick++);
        Assert.False(timed.Advance()); Assert.Equal(1001, visited);
        timed.Dispose(); Assert.True(timed.Advance());
    }

    [Fact]
    public void ProjectionRejectsInvalidVersionQuotasAndChangedIdentity()
    {
        static IEnumerator<(string, CatalogItem)> Rows() => Enumerable.Empty<(string, CatalogItem)>().GetEnumerator();
        Assert.Throws<InvalidOperationException>(() => new BoundedCatalogProjection("English", " ", Rows(), () => ("", ""), () => 0));
        var identity = ("English", "build");
        using var builder = new BoundedCatalogProjection("English", "build", Rows(), () => identity, () => 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Advance(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Advance(2049));
        identity = ("Japanese", "build"); Assert.Throws<InvalidOperationException>(() => builder.Advance());
        identity = ("English", "new"); Assert.Throws<InvalidOperationException>(() => builder.Advance());
    }

    [Fact]
    public void ProductItemProjectionSuppliesDresserNamesAndFreshPresenceSelectsExactRows()
    {
        var now = DateTimeOffset.Parse("2026-09-06T00:00:00Z");
        var itemRows = new[]
        {
            CatalogRowPolicy.Project(new("@items", 0, "Invalid")),
            CatalogRowPolicy.Project(new("@items", 100, "Stored coat")),
            CatalogRowPolicy.Project(new("@items", 200, "Not stored")),
        };
        using var projection = new BoundedCatalogProjection("English", "build", itemRows.AsEnumerable().GetEnumerator(),
            () => ("English", "build"), () => 0);
        Assert.True(projection.Advance());
        var catalog = CollectionCatalogBuilderPolicy.Build(projection.Projection!);
        Assert.Equal(new uint[] { 100, 200 }, catalog.Collections["glamour_dresser"].Items.Select(item => item.Id));
        var state = CollectionStateFactory.Empty(1, Guid.NewGuid(), now);
        state = state with
        {
            CurrentItemPresence = state.CurrentItemPresence.SetItem("glamourDresser",
            CaptureReadPolicy.Presence([new(100, null)], false, true, 1, now))
        };
        var rich = CollectionDataAssembler.Assemble(catalog, state).Collections["glamour_dresser"];
        var item = Assert.Single(rich.Items);
        Assert.Equal(100u, item.Id); Assert.Equal("Stored coat", item.Name); Assert.True(item.Owned); Assert.False(rich.TotalKnown);
        state = state with
        {
            CurrentItemPresence = state.CurrentItemPresence.SetItem("glamourDresser",
            CaptureReadPolicy.Presence([], false, true, 2, now.AddSeconds(1)))
        };
        Assert.Empty(CollectionDataAssembler.Assemble(catalog, state).Collections["glamour_dresser"].Items);
    }

    [Fact]
    public void NotebookMappingUsesNonzeroSeriesOrderAndExactSlots()
    {
        NotebookSeries[] rows = [new(0, [1]), new(10, [1, 2]), new(20, [3, 4])];
        Assert.Equal(new NotebookSlot(1, 1), NotebookMappingPolicy.Resolve(4, rows, 2, 2));
        Assert.Equal(1, NotebookMappingPolicy.Resolve(4, rows, 2, 2).Series);
        Assert.Equal(1, NotebookMappingPolicy.Resolve(4, rows, 2, 2).Slot);
        Assert.Equal(new NotebookSlot(0, 0), NotebookMappingPolicy.Resolve(1, rows, 2, 2));
        Assert.Equal(ReasonCode.MappingValidationFailed, Assert.Throws<SourceReadException>(() => NotebookMappingPolicy.Resolve(4, rows, 1, 2)).Code);
        Assert.Throws<SourceReadException>(() => NotebookMappingPolicy.Resolve(4, rows, 2, 1));
        Assert.Throws<SourceReadException>(() => NotebookMappingPolicy.Resolve(5, rows, 2, 2));
        Assert.Equal(new QuestState(1, QuestStatus.Completed, 9), NotebookMappingPolicy.Quest(1, true, true, 9));
        Assert.Equal(new QuestState(1, QuestStatus.InProgress, 9), NotebookMappingPolicy.Quest(1, false, true, 9));
        Assert.Equal(new QuestState(1, QuestStatus.NotStarted, null), NotebookMappingPolicy.Quest(1, false, false, 9));
        Assert.Equal(new QuestState(1, QuestStatus.Completed, null), NotebookMappingPolicy.Quest(1, true, false, null));
    }
}
