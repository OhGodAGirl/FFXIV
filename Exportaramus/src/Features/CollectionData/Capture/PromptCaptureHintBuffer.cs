namespace Exportaramus.Features.CollectionData.Capture;

/// <summary>Bounded, coalesced hints; these never establish ownership or observed quantities.</summary>
public sealed class PromptCaptureHintBuffer : IDisposable
{
    private static readonly string[] Sources =
        ["mounts", "minions", "emotes", "bardings", "cards", "fashions", "frames",
         "occult_records", "orchestrions", "spells", "aetherytes", "chocobo_routes",
         "leves", "hairstyles", "field_records", "survey_records", "quests", "achievements",
         "facewear", "inventory", "armoryChest", "chocoboSaddlebag", "premiumSaddlebag", "retainers"];
    private const int UnlockMask = (1 << 19) - 1;
    private const int PresenceMask = ((1 << 24) - 1) & ~UnlockMask;
    private int pending;
    private int disposed;
    public void Drain(Action<string> hint)
    {
        var mask = Interlocked.Exchange(ref pending, 0);
        if (Volatile.Read(ref disposed) != 0) return;
        for (var i = 0; i < Sources.Length; i++)
            if ((mask & (1 << i)) != 0) hint(Sources[i]);
    }
    public void Reset() => Interlocked.Exchange(ref pending, 0);
    public void Dispose()
    {
        Interlocked.Exchange(ref disposed, 1);
        Reset();
    }
    private void Queue(int mask)
    {
        if (Volatile.Read(ref disposed) == 0) Interlocked.Or(ref pending, mask);
    }
    public void Unlock(string rowType)
    {
        var index = rowType switch
        {
            "Mount" => 0,
            "Companion" => 1,
            "Emote" => 2,
            "BuddyEquip" => 3,
            "TripleTriadCard" => 4,
            "Ornament" => 5,
            "BannerCondition" or "BannerFrame" or "BannerBg" or "BannerDecoration" => 6,
            "MKDLore" => 7,
            "Orchestrion" => 8,
            "AozAction" => 9,
            "Aetheryte" => 10,
            "ChocoboTaxiStand" or "ChocoboTaxi" => 11,
            "Leve" => 12,
            "CharaMakeCustomize" => 13,
            "Quest" => 16,
            "Achievement" => 17,
            "Glasses" or "GlassesStyle" => 18,
            _ => -1,
        };
        if (index >= 0) Queue(1 << index);
        else if (rowType is "Item" or "McGuffin") Queue(UnlockMask);
    }
    public void Inventory(IEnumerable<string> containers)
    {
        int mask = 0, count = 0;
        foreach (var container in containers)
        {
            if (++count > 256)
            {
                mask |= PresenceMask | UnlockMask;
                break;
            }
            int index = container switch
            {
                "Inventory1" or "Inventory2" or "Inventory3" or
                    "Inventory4" or "Crystals" or "Currency" => 19,
                "EquippedItems" or "ArmoryMainHand" or "ArmoryOffHand" or
                    "ArmoryHead" or "ArmoryBody" or "ArmoryHands" or
                    "ArmoryLegs" or "ArmoryFeets" or "ArmoryEar" or
                    "ArmoryNeck" or "ArmoryWrist" or "ArmoryRings" or
                    "ArmorySoulCrystal" => 20,
                "SaddleBag1" or "SaddleBag2" => 21,
                "PremiumSaddleBag1" or "PremiumSaddleBag2" => 22,
                "RetainerPage1" or "RetainerPage2" or "RetainerPage3" or
                    "RetainerPage4" or "RetainerPage5" or "RetainerPage6" or
                    "RetainerPage7" or "RetainerEquippedItems" or
                    "RetainerCrystals" or "RetainerGil" or "RetainerMarket" => 23,
                _ => -1,
            };
            if (index >= 0) mask |= 1 << index;
        }
        Queue(mask);
    }
}
