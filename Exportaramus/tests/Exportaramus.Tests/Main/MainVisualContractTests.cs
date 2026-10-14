using System.Collections.Immutable;
using Exportaramus.Features.GuidedExport;
using Exportaramus.Features.Main;

namespace Exportaramus.Tests.Main;

public sealed class MainVisualContractTests
{
    [Fact]
    public void SummaryPreservesAllPlayerLabelsAndPopulatedIconSlotsFromFrontendSection74()
    {
        var now = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
        var input = new MainPresentationInput(1, MainStatus.Current, true, true, false, true, 0, now, now, "", "Ready To Share",
            UiTone.Success, [], ImmutableDictionary<string, CollectionRowObservation>.Empty,
            new(GuidedState.Idle, "Ready", "", false, false, false, false, false));

        var view = MainPresenter.Prepare(input, now);

        Assert.Equal(new[] { "Aetherytes", "Armoire", "Bardings", "Triple Triad Cards", "Chocobo Routes", "Emotes",
            "Fashion Accessories", "Field Records", "Portrait Frames", "Glamour Dresser", "Hairstyles", "Levequests",
            "Minions", "Mounts", "Triple Triad NPCs", "Occult Records", "Orchestrion Rolls", "Outfits", "Quests",
            "Relics", "Blue Magic Spells", "Variant Dungeon Records", "Achievements" }, view.Rows.Select(row => row.Label));
        Assert.All(view.Rows, row => Assert.False(string.IsNullOrWhiteSpace(row.Icon)));
    }
}
