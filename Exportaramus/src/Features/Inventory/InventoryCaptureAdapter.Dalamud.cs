using Dalamud.Plugin.Services;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Exportaramus.Features.Inventory;

// Independent passive capture. Update is called by the host only on the framework thread.
public sealed unsafe class InventoryCaptureAdapter
{
    private readonly InventoryService service;
    private readonly ICollectionReader collections;
    private readonly IDataManager data;
    private readonly IPlayerState player;
    private readonly IFramework framework;
    private readonly IGameGui gui;
    private readonly InventoryContainerReader containers;
    private ulong character;
    private long nextRead;
    private int nextLocation;
    private long generation;

    public InventoryCaptureAdapter(InventoryService service, ICollectionReader collections, IDataManager data,
        IPlayerState player, IFramework framework, IGameGui gui)
    {
        this.service = service; this.collections = collections; this.data = data;
        this.player = player; this.framework = framework; this.gui = gui;
        containers = new(data);
    }

    public void Update(CollectionCatalog? catalog)
    {
        if (!framework.IsInFrameworkUpdateThread) return;
        var contentId = player.IsLoaded ? player.ContentId : 0;
        if (character != contentId)
        {
            character = contentId; nextRead = 0; nextLocation = 0;
        }
        if (contentId == 0 || Environment.TickCount64 < nextRead) return;
        nextRead = Environment.TickCount64 + 200;
        var location = Locations[nextLocation];
        nextLocation = (nextLocation + 1) % Locations.Length;
        var now = DateTimeOffset.UtcNow;
        var key = new InventoryLocationKey(location.Kind);
        try
        {
            var version = data.GameData.Repositories.Values.Single(r => r.ExpansionId == 0).Version;
            if (!ReaderCompatibility.SourcesFor(version).Contains(location.Source))
            {
                if (location.Kind != InventoryLocationKind.Retainer)
                    service.MarkUnavailable(contentId, key, location.Name, now, true);
                return;
            }
            if (location.Kind == InventoryLocationKind.Retainer)
            {
                ReadRetainer(contentId, now);
                return;
            }
            if (location.Types.Length != 0)
            {
                var items = containers.Read(location.Types);
                if (!IdentityMatches(contentId)) return;
                if (items is null) service.MarkUnavailable(contentId, key, location.Name, now, false);
                else service.AcceptLocation(contentId, key, location.Name, items, now);
                return;
            }
            // The reviewed storage readers expose membership, including unlocked dresser set slots.
            // Zero quantities explicitly mean membership-only, never an invented normal-quality copy.
            if (catalog is null || catalog.GameDataVersion != version) return;
            var presence = collections.ReadPresence(location.Source, catalog, contentId, ++generation, now);
            if (!IdentityMatches(contentId)) return;
            if (presence.Health.IsUsable)
                service.AcceptLocation(contentId, key, location.Name,
                    presence.Items.Select(item => new InventoryItemQuantity(item.ItemId, 0, 0)), now);
            else service.MarkUnavailable(contentId, key, location.Name, now, false);
        }
        catch (Exception)
        {
            if (IdentityMatches(contentId) && location.Kind != InventoryLocationKind.Retainer)
                service.MarkUnavailable(contentId, key, location.Name, now, true);
        }
    }

    public bool CaptureActiveRetainer(ulong contentId, ulong expectedRetainerId)
    {
        if (!IdentityMatches(contentId) || expectedRetainerId == 0) return false;
        var version = data.GameData.Repositories.Values.Single(r => r.ExpansionId == 0).Version;
        if (!ReaderCompatibility.SourcesFor(version).Contains("retainers")) return false;
        return ReadRetainer(contentId, DateTimeOffset.UtcNow, expectedRetainerId) == expectedRetainerId;
    }

    private ulong ReadRetainer(ulong contentId, DateTimeOffset now, ulong expectedRetainerId = 0)
    {
        var manager = RetainerManager.Instance();
        if (manager == null || !manager->IsReady) return 0;
        var count = manager->GetRetainerCount();
        if (count > 10) return 0;
        var roster = new List<InventoryRetainer>(count);
        var ids = new HashSet<ulong>();
        for (uint i = 0; i < count; i++)
        {
            var row = manager->GetRetainerBySortedIndex(i);
            if (row == null || !row->Available || row->RetainerId == 0 || !ids.Add(row->RetainerId) || !InventoryPolicy.ValidName(row->NameString)) return 0;
            roster.Add(new(row->RetainerId, row->NameString));
        }
        var active = manager->GetActiveRetainer();
        var activeId = active == null ? 0 : active->RetainerId;
        if (expectedRetainerId != 0 && activeId != expectedRetainerId) return 0;
        var addonName = "InventoryRetainer";
        var addon = gui.GetAddonByName<AtkUnitBase>(addonName, 1);
        if (!Ready(addon))
        {
            addonName = "InventoryRetainerLarge";
            addon = gui.GetAddonByName<AtkUnitBase>(addonName, 1);
        }
        var canRead = ids.Contains(activeId) && Ready(addon);
        List<InventoryItemQuantity>? items = canRead ? containers.Read(RetainerContainers) : null;
        if (!IdentityMatches(contentId) || manager != RetainerManager.Instance() || !manager->IsReady || count != manager->GetRetainerCount()) return 0;
        for (uint i = 0; i < count; i++)
        {
            var row = manager->GetRetainerBySortedIndex(i);
            if (row == null || !row->Available || row->RetainerId != roster[(int)i].RetainerId || row->NameString != roster[(int)i].Name) return 0;
        }
        service.ObserveRoster(contentId, roster, now);
        if (!canRead) return 0;
        var finalActive = manager->GetActiveRetainer();
        if (finalActive == null || finalActive->RetainerId != activeId
            || addon != gui.GetAddonByName<AtkUnitBase>(addonName, 1) || !Ready(addon)) return 0;
        var name = roster.Single(row => row.RetainerId == activeId).Name;
        var key = new InventoryLocationKey(InventoryLocationKind.Retainer, activeId);
        if (items is null) service.MarkUnavailable(contentId, key, name, now, false);
        else service.AcceptLocation(contentId, key, name, items, now);
        return items is not null ? activeId : 0;
    }

    private bool IdentityMatches(ulong contentId) => framework.IsInFrameworkUpdateThread && player.IsLoaded && player.ContentId == contentId;
    private static bool Ready(AtkUnitBase* addon) => addon != null && addon->IsReady && addon->IsVisible;

    private static readonly InventoryType[] RetainerContainers =
        [InventoryType.RetainerPage1, InventoryType.RetainerPage2, InventoryType.RetainerPage3,
         InventoryType.RetainerPage4, InventoryType.RetainerPage5, InventoryType.RetainerPage6,
         InventoryType.RetainerPage7, InventoryType.RetainerEquippedItems, InventoryType.RetainerCrystals];

    private static readonly (InventoryLocationKind Kind, string Source, string Name, InventoryType[] Types)[] Locations =
    [
        (InventoryLocationKind.Inventory, "inventory", "Inventory", [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4, InventoryType.Crystals]),
        (InventoryLocationKind.Equipped, "armoryChest", "Equipped", [InventoryType.EquippedItems]),
        (InventoryLocationKind.ArmoryChest, "armoryChest", "Armoury Chest", [InventoryType.ArmoryMainHand, InventoryType.ArmoryOffHand,
            InventoryType.ArmoryHead, InventoryType.ArmoryBody, InventoryType.ArmoryHands, InventoryType.ArmoryLegs,
            InventoryType.ArmoryFeets, InventoryType.ArmoryEar, InventoryType.ArmoryNeck, InventoryType.ArmoryWrist,
            InventoryType.ArmoryRings, InventoryType.ArmorySoulCrystal]),
        (InventoryLocationKind.ChocoboSaddlebag, "chocoboSaddlebag", "Chocobo Saddlebag", [InventoryType.SaddleBag1, InventoryType.SaddleBag2]),
        (InventoryLocationKind.PremiumSaddlebag, "premiumSaddlebag", "Premium Saddlebag", [InventoryType.PremiumSaddleBag1, InventoryType.PremiumSaddleBag2]),
        (InventoryLocationKind.Retainer, "retainers", "Retainers", []),
        (InventoryLocationKind.Armoire, "armoire", "Armoire", []),
        (InventoryLocationKind.GlamourDresser, "glamourDresser", "Glamour Dresser", []),
    ];
}
