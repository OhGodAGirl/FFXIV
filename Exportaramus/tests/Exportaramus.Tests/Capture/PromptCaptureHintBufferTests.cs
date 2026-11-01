using Exportaramus.Features.CollectionData.Capture;

namespace Exportaramus.Tests.Capture;

public sealed class PromptCaptureHintBufferTests
{
    [Theory]
    [InlineData("Mount", "mounts")]
    [InlineData("Companion", "minions")]
    [InlineData("Emote", "emotes")]
    [InlineData("BuddyEquip", "bardings")]
    [InlineData("TripleTriadCard", "cards")]
    [InlineData("Ornament", "fashions")]
    [InlineData("BannerCondition", "frames")]
    [InlineData("BannerFrame", "frames")]
    [InlineData("BannerBg", "frames")]
    [InlineData("BannerDecoration", "frames")]
    [InlineData("MKDLore", "occult_records")]
    [InlineData("Orchestrion", "orchestrions")]
    [InlineData("AozAction", "spells")]
    [InlineData("Aetheryte", "aetherytes")]
    [InlineData("ChocoboTaxiStand", "chocobo_routes")]
    [InlineData("ChocoboTaxi", "chocobo_routes")]
    [InlineData("Leve", "leves")]
    [InlineData("CharaMakeCustomize", "hairstyles")]
    [InlineData("Quest", "quests")]
    [InlineData("Achievement", "achievements")]
    [InlineData("Glasses", "facewear")]
    [InlineData("GlassesStyle", "facewear")]
    public void UnlockRowsMapToTheirCollection(string row, string expected)
    {
        using var hints = new PromptCaptureHintBuffer();
        hints.Unlock(row);
        Assert.Equal([expected], Drain(hints));
        Assert.Empty(Drain(hints));
    }
    [Theory]
    [InlineData("Inventory1", "inventory")]
    [InlineData("Inventory2", "inventory")]
    [InlineData("Inventory3", "inventory")]
    [InlineData("Inventory4", "inventory")]
    [InlineData("Crystals", "inventory")]
    [InlineData("Currency", "inventory")]
    [InlineData("EquippedItems", "armoryChest")]
    [InlineData("ArmoryMainHand", "armoryChest")]
    [InlineData("ArmoryOffHand", "armoryChest")]
    [InlineData("ArmoryHead", "armoryChest")]
    [InlineData("ArmoryBody", "armoryChest")]
    [InlineData("ArmoryHands", "armoryChest")]
    [InlineData("ArmoryLegs", "armoryChest")]
    [InlineData("ArmoryFeets", "armoryChest")]
    [InlineData("ArmoryEar", "armoryChest")]
    [InlineData("ArmoryNeck", "armoryChest")]
    [InlineData("ArmoryWrist", "armoryChest")]
    [InlineData("ArmoryRings", "armoryChest")]
    [InlineData("ArmorySoulCrystal", "armoryChest")]
    [InlineData("SaddleBag1", "chocoboSaddlebag")]
    [InlineData("SaddleBag2", "chocoboSaddlebag")]
    [InlineData("PremiumSaddleBag1", "premiumSaddlebag")]
    [InlineData("PremiumSaddleBag2", "premiumSaddlebag")]
    [InlineData("RetainerPage1", "retainers")]
    [InlineData("RetainerPage2", "retainers")]
    [InlineData("RetainerPage3", "retainers")]
    [InlineData("RetainerPage4", "retainers")]
    [InlineData("RetainerPage5", "retainers")]
    [InlineData("RetainerPage6", "retainers")]
    [InlineData("RetainerPage7", "retainers")]
    [InlineData("RetainerEquippedItems", "retainers")]
    [InlineData("RetainerCrystals", "retainers")]
    [InlineData("RetainerGil", "retainers")]
    [InlineData("RetainerMarket", "retainers")]
    public void ContainersMapToTheirLocation(string container, string expected)
    {
        using var hints = new PromptCaptureHintBuffer();
        hints.Inventory([container]);
        Assert.Equal([expected], Drain(hints));
    }
    [Theory]
    [InlineData("Item")]
    [InlineData("McGuffin")]
    public void IndirectUnlocksInvalidateAllUnlockHints(string row)
    {
        using var hints = new PromptCaptureHintBuffer();
        hints.Unlock(row);
        Assert.Equal(UnlockSources, Drain(hints));
    }
    [Fact]
    public void MixedDuplicatesUnknownEventsAndEmptyBatchesStayBounded()
    {
        using var hints = new PromptCaptureHintBuffer();
        hints.Unlock("Unknown");
        hints.Inventory(["Unknown"]);
        hints.Inventory([]);
        Assert.Empty(Drain(hints));
        hints.Unlock("Mount");
        hints.Unlock("Mount");
        hints.Inventory(["Inventory1", "Inventory2", "SaddleBag1"]);
        Assert.Equal(["mounts", "inventory", "chocoboSaddlebag"], Drain(hints));
    }
    [Fact]
    public void BurstLimitDistinguishes256And257AndStopsEnumeration()
    {
        using var hints = new PromptCaptureHintBuffer();
        hints.Inventory(Enumerable.Repeat("Inventory1", 256));
        Assert.Equal(["inventory"], Drain(hints));
        hints.Inventory(Burst());
        Assert.Equal(UnlockSources.Concat(["inventory", "armoryChest", "chocoboSaddlebag", "premiumSaddlebag", "retainers"]), Drain(hints));
        static IEnumerable<string> Burst()
        {
            for (var i = 0; i < 257; i++) yield return "Unknown";
            throw new InvalidOperationException("The overflow hint must stop enumeration.");
        }
    }
    [Fact]
    public void ResetAndDisposeDiscardQueuedAndLaterHints()
    {
        var hints = new PromptCaptureHintBuffer();
        hints.Unlock("Mount");
        hints.Reset();
        Assert.Empty(Drain(hints));
        hints.Unlock("Mount");
        hints.Dispose();
        hints.Unlock("Item");
        hints.Inventory(["Inventory1"]);
        Assert.Empty(Drain(hints));
        hints.Dispose();
    }
    private static string[] Drain(PromptCaptureHintBuffer hints)
    {
        var result = new List<string>();
        hints.Drain(result.Add);
        return result.ToArray();
    }
    private static readonly string[] UnlockSources = ["mounts", "minions", "emotes", "bardings", "cards", "fashions", "frames",
        "occult_records", "orchestrions", "spells", "aetherytes", "chocobo_routes", "leves", "hairstyles", "field_records",
        "survey_records", "quests", "achievements", "facewear"];
}
