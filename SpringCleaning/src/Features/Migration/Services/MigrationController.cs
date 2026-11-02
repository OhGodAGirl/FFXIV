using System.Numerics;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using SpringCleaning.Shared.CollectionData;

namespace SpringCleaning;

internal sealed class MigrationController
{
    private const string DresserName = "Glamour Dresser";
    private const string ArmoireName = "Armoire";
    private const int MinimumFilterRefreshMilliseconds = 750;
    private const int MinimumNativeConfirmationMilliseconds = 15_000;
    private const int MaximumRememberedStationIds = 256;
    private static readonly TimeSpan PreviewRefreshInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan DresserListRefreshTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan NativeOperationTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan InteractionRetryInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan InteractionSettleInterval = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan DresserOpenRetryInterval = TimeSpan.FromMilliseconds(1_500);
    private static readonly TimeSpan DresserReadyInterval = TimeSpan.FromMilliseconds(2_000);
    private static readonly TimeSpan UiRestoreInterval = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan ShortActionInterval = TimeSpan.FromMilliseconds(150);
    private readonly Configuration configuration;
    private readonly NativePhaseDeadline nativePhaseDeadline = new();
    private readonly DresserScanner scanner;
    private readonly CollectionDataRefreshController collectionData;
    private readonly NativeUi nativeUi;
    private readonly VNavmeshIpc navmesh;
    private readonly IObjectTable objectTable;
    private readonly ITargetManager targetManager;
    private readonly IClientState clientState;
    private readonly IPluginLog log;
    private readonly HashSet<uint> dresserStationIds;
    private readonly HashSet<uint> armoireStationIds;
    private readonly Queue<CabinetInventoryItem> withdrawnBatch = new();
    private readonly Queue<InventoryDresserOutfit> dresserOutfits = new();
    private readonly Queue<InventoryDresserItem> dresserItems = new();
    private readonly List<int> dresserSourceCategoryIndexes = new();
    private readonly List<InventoryDresserCandidate> dresserSourceCandidates = new();
    private readonly HashSet<uint> dresserArmoirePlanItemIds = new();
    private IGameObject? dresserObject;
    private IGameObject? armoireObject;
    private PendingWithdrawal? pendingWithdrawal;
    private PendingDeposit? pendingDeposit;
    private PendingDresserOutfit? pendingDresserOutfit;
    private PendingDresserItem? pendingDresserItem;
    private int batchTarget;
    private int batchWithdrawn;
    private bool navigationIssued;
    private bool interactionIssued;
    private bool menuSelectionIssued;
    private bool dresserConfirmationIssued;
    private bool dresserItemConfirmationIssued;
    private int interactionAttempts;
    private DateTime nextActionAt;
    private DateTime nextPreviewAt;
    private DateTime nextInteractionAt;
    private DateTime dresserPreparationDeadline;
    private DateTime nextDresserOpenAttemptAt;
    private DateTime dresserReadyAfter;
    private DateTime dresserFilterRefreshReadyAfter;
    private DateTime dresserInventoryScanDeadline;
    private DateTime dresserItemOpenDeadline;
    private int dresserOpenAttempts;
    private bool? originalGearsetItemsHidden;
    private int? originalDresserCategoryIndex;
    private int dresserSourceCategoryPosition;
    private DateTime dresserUiRestoreReadyAfter;
    private bool wasDresserAddonOpen;
    private bool wasArmoireAddonOpen;

    public string Status { get; private set; } = "Open the Glamour Dresser to begin.";

    public IReadOnlyList<DresserAlertEntry> Alerts { get; private set; } = Array.Empty<DresserAlertEntry>();

    // Presentation may hold its existing rows briefly; every command still uses LatestSnapshot.
    public bool RetainPresentationDuringRefresh { get; set; }

    public MigrationController(
                Configuration configuration,
                DresserScanner scanner,
                CollectionDataRefreshController collectionData,
                NativeUi nativeUi,
                VNavmeshIpc navmesh,
                IObjectTable objectTable,
                ITargetManager targetManager,
                IClientState clientState,
                IDataManager dataManager,
                IPluginLog log)
    {
        this.configuration = configuration;
        this.scanner = scanner;
        this.collectionData = collectionData;
        this.nativeUi = nativeUi;
        this.navmesh = navmesh;
        this.objectTable = objectTable;
        this.targetManager = targetManager;
        this.clientState = clientState;
        this.log = log;
        dresserStationIds = ResolveHousingStationIds(dataManager, DresserName);
        armoireStationIds = ResolveHousingStationIds(dataManager, ArmoireName);
    }

    public MigrationPhase Phase { get; private set; }

    public MigrationMode Mode { get; private set; }

    public string? CurrentItemName { get; private set; }

    public int EligibleCount { get; private set; }

    public int DresserPrismsRequired { get; private set; }

    public uint DresserPrismsHeld { get; private set; }

    public bool IsDresserPrismCountReady { get; private set; }

    public int StoredCount { get; private set; }

    public int TotalPieceCount { get; private set; }

    public bool IsActive => Phase is not MigrationPhase.Idle
        and not MigrationPhase.Complete
        and not MigrationPhase.Paused
        and not MigrationPhase.Cancelled;

    public string OwnershipReadiness(params CollectionSourceName[] sources) =>
        CollectionOwnershipPolicy.ReadinessMessage(collectionData.LatestSnapshot, sources);

    public bool HasFreshDresserArmoireOwnership => DresserCompositionPolicy.IsReady(collectionData.LatestSnapshot)
        && CollectionOwnershipPolicy.AreSourcesFresh(
        collectionData.LatestSnapshot,
        CollectionSourceName.GlamourDresser,
        CollectionSourceName.Armoire);

    public bool HasFreshInventoryArmoireOwnership => CollectionOwnershipPolicy.AreSourcesFresh(
        collectionData.LatestSnapshot,
        CollectionSourceName.Inventory,
        CollectionSourceName.Armoire);

    public bool HasFreshDuplicateOwnership =>
        collectionData.LatestSnapshot is { } snapshot
        && (snapshot.Inventory.Status == CollectionSourceStatus.Fresh
            || snapshot.ArmoryChest.Status == CollectionSourceStatus.Fresh)
        && (snapshot.GlamourDresser.Status is CollectionSourceStatus.Fresh or CollectionSourceStatus.Stale
            || snapshot.Armoire.Status is CollectionSourceStatus.Fresh or CollectionSourceStatus.Stale);

    public bool HasFreshInventoryDresserOwnership => DresserCompositionPolicy.IsReady(collectionData.LatestSnapshot)
        && CollectionOwnershipPolicy.AreSourcesFresh(
        collectionData.LatestSnapshot,
        CollectionSourceName.Inventory,
        CollectionSourceName.GlamourDresser);

    public bool HasFreshArmouryDresserOwnership => DresserCompositionPolicy.IsReady(collectionData.LatestSnapshot)
        && CollectionOwnershipPolicy.AreSourcesFresh(
        collectionData.LatestSnapshot,
        CollectionSourceName.ArmoryChest,
        CollectionSourceName.GlamourDresser);

    public bool HasFreshDresserStorageOwnership => DresserCompositionPolicy.IsReady(collectionData.LatestSnapshot)
        && CollectionOwnershipPolicy.AreSourcesFresh(
        collectionData.LatestSnapshot,
        CollectionSourceName.Inventory,
        CollectionSourceName.ArmoryChest,
        CollectionSourceName.Armoire,
        CollectionSourceName.GlamourDresser);

    public bool CanStart => nativeUi.IsDresserOpen
        && scanner.IsDresserLoaded
        && HasFreshDresserArmoireOwnership
        && !IsActive
        && !IsDresserUiRestorePending;

    public bool CanStartInventory => clientState.IsLoggedIn
        && HasFreshDresserStorageOwnership
        && !IsActive
        && !IsDresserUiRestorePending;

    public bool CanStartInventoryArmoire => clientState.IsLoggedIn
        && HasFreshInventoryArmoireOwnership
        && !IsActive
        && !IsDresserUiRestorePending;

    public bool CanStartInventoryDresser => nativeUi.IsDresserOpen
        && scanner.IsDresserLoaded
        && HasFreshInventoryDresserOwnership
        && !IsActive
        && !IsDresserUiRestorePending;

    public bool CanStartArmouryDresser => nativeUi.IsDresserOpen
        && scanner.IsDresserLoaded
        && HasFreshArmouryDresserOwnership
        && !IsActive
        && !IsDresserUiRestorePending;

    public bool HasEnoughDresserPrisms => DresserPrismPolicy.Evaluate(
        DresserPrismsHeld,
        DresserPrismsRequired,
        IsDresserPrismCountReady).HasEnough;

    public int MissingDresserPrisms => DresserPrismPolicy.Evaluate(
        DresserPrismsHeld,
        DresserPrismsRequired,
        IsDresserPrismCountReady).Missing;

    internal SupportDiagnostics SupportDiagnostics => nativeUi.Diagnostics;

    internal MigrationDiagnosticSnapshot CaptureDiagnostics() => new(
        Plugin.DevBuildIdentity,
        Mode,
        Phase,
        Status,
        CurrentItemName,
        StoredCount,
        TotalPieceCount,
        EligibleCount,
        DresserPrismsHeld,
        DresserPrismsRequired,
        IsDresserPrismCountReady,
        scanner.IsDresserLoaded,
        scanner.IsCabinetLoaded,
        scanner.AreDresserInventoryCandidatesReady,
        nativeUi.IsDresserOpen,
        nativeUi.IsArmoireStoreOpen,
        nativeUi.IsGlamourCreationOpen,
        nativeUi.IsOutfitCreationOpen,
        nativeUi.IsOutfitConfirmationOpen,
        nativeUi.IsDresserItemConfirmationOpen,
        nativeUi.IsInventoryContextOpen,
        nativeUi.IsExistingOutfitMergePromptOpen,
        dresserOutfits.Count,
        dresserItems.Count,
        dresserSourceCategoryPosition + 1,
        dresserSourceCategoryIndexes.Count,
        interactionAttempts,
        dresserOpenAttempts,
        navigationIssued,
        interactionIssued,
        menuSelectionIssued,
        dresserConfirmationIssued || dresserItemConfirmationIssued);

    public void ClearPreviewPresentation()
    {
        Alerts = [];
        nextPreviewAt = DateTime.MinValue;
    }

    public void RefreshPreview()
    {
        if (RetainPresentationDuringRefresh
            || !nativeUi.IsDresserOpen
            || !scanner.IsDresserLoaded
            || IsActive
            || DateTime.UtcNow < nextPreviewAt)
            return;

        nextPreviewAt = DateTime.UtcNow.Add(PreviewRefreshInterval);
        var prismCountReady = scanner.TryGetGlamourPrismCount(out var prismCount);
        DresserPrismsHeld = prismCount;
        Alerts = scanner.PlanAlerts(collectionData.LatestSnapshot);
        EligibleCount = scanner.PlanEligibleDresserItems(collectionData.LatestSnapshot).Count;
        if (scanner.AreDresserInventoryCandidatesReady)
        {
            IsDresserPrismCountReady = prismCountReady;
            DresserPrismsRequired = scanner
                .ScanEligibleInventoryDresserOutfits(
                    collectionData.LatestSnapshot,
                    configuration.OnlyMoveCompleteOutfits)
                .Sum(outfit => outfit.Components.Count)
                + scanner.ScanEligibleInventoryDresserItems(collectionData.LatestSnapshot).Count;
        }
        else
        {
            IsDresserPrismCountReady = false;
            DresserPrismsRequired = 0;
        }
        if (Phase == MigrationPhase.Idle)
        {
            Status = EligibleCount == 0
                ? "No Armoire-eligible items were found."
                : $"{EligibleCount} eligible item{(EligibleCount == 1 ? string.Empty : "s")} found.";
        }
    }

    public void ObserveOpenStations()
    {
        var dresserOpen = nativeUi.IsDresserOpen;
        var armoireOpen = nativeUi.IsArmoireStoreOpen;
        var target = targetManager.Target;
        if (target is not null && target.IsValid())
        {
            if (dresserOpen && !wasDresserAddonOpen)
                RememberStation(dresserStationIds, target);
            if (armoireOpen && !wasArmoireAddonOpen)
                RememberStation(armoireStationIds, target);
        }

        wasDresserAddonOpen = dresserOpen;
        wasArmoireAddonOpen = armoireOpen;
    }

    public void Restore(DresserAlertEntry entry)
    {
        if (IsActive)
            return;
        if (scanner.GetEmptyInventorySlots() == 0)
        {
            Status = "No inventory space is available for that item.";
            return;
        }

        var current = scanner.PlanAlerts(collectionData.LatestSnapshot)
            .FirstOrDefault(candidate => candidate.PrismBoxIndex == entry.PrismBoxIndex
                && candidate.RawItemId == entry.RawItemId);
        if (current is null)
        {
            Status = "Waiting for the current Dresser entry from Exportaramus.";
            return;
        }
        Status = scanner.TryRestore(current)
            ? $"Restoring {entry.Name}…"
            : $"The game declined to restore {entry.Name}.";
        nextPreviewAt = DateTime.UtcNow.AddMilliseconds(configuration.ActionDelayMilliseconds);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(configuration.ActionDelayMilliseconds);
    }

    public void Start()
    {
        if (!HasFreshDresserArmoireOwnership)
        {
            Pause("Wait for fresh Dresser and Armoire ownership data from Exportaramus.");
            return;
        }
        if (!CanStart)
        {
            Pause("Open the Glamour Dresser and wait for its contents to load.");
            return;
        }

        Mode = MigrationMode.DresserToArmoire;
        dresserObject = FindCurrentOrNamedObject(DresserName);
        armoireObject = FindNamedObject(ArmoireName);
        if (dresserObject is null)
        {
            Pause("The Glamour Dresser furnishing could not be identified. Target it and try again.");
            return;
        }
        if (armoireObject is null)
        {
            Pause("No nearby Armoire furnishing was found.");
            return;
        }

        withdrawnBatch.Clear();
        pendingWithdrawal = null;
        pendingDeposit = null;
        StoredCount = 0;
        var planned = scanner.PlanEligibleDresserItems(collectionData.LatestSnapshot);
        dresserArmoirePlanItemIds.Clear();
        dresserArmoirePlanItemIds.UnionWith(
            planned.SelectMany(item => item.Components).Select(component => component.ItemId));
        var eligible = scanner.RevalidateEligibleItemsForAction(collectionData.LatestSnapshot, dresserArmoirePlanItemIds);
        EligibleCount = eligible.Count;
        TotalPieceCount = eligible.Sum(item => item.InventorySlotsRequired);
        Phase = scanner.IsCabinetLoaded ? MigrationPhase.Scanning : MigrationPhase.ClosingDresser;
        Status = scanner.IsCabinetLoaded
            ? "Preparing the first inventory batch."
            : "Loading the Armoire state before withdrawing anything.";
        nextActionAt = DateTime.UtcNow;
        ResetMovementFlags();
    }

    public void StartInventoryStore()
    {
        if (!HasFreshDresserStorageOwnership)
        {
            Pause("Wait for fresh inventory, Armoury Chest, Dresser, and Armoire ownership data from Exportaramus.");
            return;
        }
        if (!CanStartInventory)
        {
            Pause("Log in before starting an inventory cleanup.");
            return;
        }

        Mode = MigrationMode.InventoryCleanup;
        armoireObject = FindCurrentOrNamedObject(ArmoireName);
        dresserObject = FindNamedObject(DresserName);
        if (armoireObject is null)
        {
            Pause("No nearby Armoire furnishing was found.");
            return;
        }
        if (dresserObject is null)
        {
            Pause("No nearby Glamour Dresser furnishing was found.");
            return;
        }

        withdrawnBatch.Clear();
        dresserOutfits.Clear();
        dresserItems.Clear();
        dresserSourceCategoryIndexes.Clear();
        dresserSourceCandidates.Clear();
        dresserSourceCategoryPosition = 0;
        pendingWithdrawal = null;
        pendingDeposit = null;
        pendingDresserOutfit = null;
        pendingDresserItem = null;
        dresserItemOpenDeadline = DateTime.MinValue;
        dresserConfirmationIssued = false;
        nativeUi.CloseInventoryContext();
        StoredCount = 0;
        EligibleCount = 0;
        TotalPieceCount = 0;
        Phase = nativeUi.IsDresserOpen
            ? MigrationPhase.ClosingDresser
            : nativeUi.IsArmoireStoreOpen
                ? MigrationPhase.OpeningArmoire
                : MigrationPhase.MovingToArmoire;
        Status = nativeUi.IsDresserOpen
            ? "Closing the Glamour Dresser before inventory cleanup."
            : "Moving to the Armoire to scan eligible inventory items.";
        nextActionAt = DateTime.UtcNow;
        ResetMovementFlags();
    }

    public void StartInventoryDresserStore() => StartDresserStore(MigrationMode.InventoryToDresser);

    public void StartArmouryDresserStore() => StartDresserStore(MigrationMode.ArmouryToDresser);

    public void StartInventoryArmoireStore()
    {
        if (!HasFreshInventoryArmoireOwnership)
        {
            Pause("Wait for fresh inventory and Armoire ownership data from Exportaramus.");
            return;
        }
        if (!CanStartInventoryArmoire)
        {
            Pause("Log in before storing inventory outfits in the Armoire.");
            return;
        }

        Mode = MigrationMode.InventoryToArmoire;
        var armoireOpen = nativeUi.IsArmoireStoreOpen;
        armoireObject = armoireOpen ? null : FindCurrentOrNamedObject(ArmoireName);
        dresserObject = null;
        if (!armoireOpen && armoireObject is null)
        {
            Pause("No nearby Armoire furnishing was found.");
            return;
        }

        withdrawnBatch.Clear();
        dresserOutfits.Clear();
        dresserItems.Clear();
        pendingWithdrawal = null;
        pendingDeposit = null;
        pendingDresserOutfit = null;
        pendingDresserItem = null;
        dresserItemOpenDeadline = DateTime.MinValue;
        dresserConfirmationIssued = false;
        StoredCount = 0;
        EligibleCount = 0;
        TotalPieceCount = 0;
        Phase = nativeUi.IsDresserOpen
            ? MigrationPhase.ClosingDresser
            : armoireOpen
                ? MigrationPhase.OpeningArmoire
                : MigrationPhase.MovingToArmoire;
        Status = nativeUi.IsDresserOpen
            ? "Closing the Glamour Dresser before storing inventory outfits in the Armoire."
            : armoireOpen
                ? "Reading the Armoire's Store an Item list."
                : "Moving to the Armoire to scan eligible inventory outfits.";
        nextActionAt = DateTime.UtcNow;
        ResetMovementFlags();
    }

    public void Cancel()
    {
        nativePhaseDeadline.Reset();
        navmesh.Stop();
        RestoreDresserUiStateIfNeeded();
        Phase = MigrationPhase.Cancelled;
        Status = "Migration cancelled. Items already moved remain safely stored.";
        CurrentItemName = null;
        pendingWithdrawal = null;
        pendingDeposit = null;
        pendingDresserOutfit = null;
        pendingDresserItem = null;
        dresserItemOpenDeadline = DateTime.MinValue;
        dresserConfirmationIssued = false;
        dresserItemConfirmationIssued = false;
    }

    public void Update(IFramework _)
    {
        if (!IsActive)
            RestoreDresserUiStateIfNeeded();

        if (!clientState.IsLoggedIn)
        {
            if (IsActive)
                Pause("Migration stopped because the character logged out.");
            return;
        }

        if (nativePhaseDeadline.Expired(Phase, DateTime.UtcNow))
        {
            Pause(Phase switch
            {
                MigrationPhase.ClosingDresser => "The Glamour Dresser did not close. Spring Cleaning stopped.",
                MigrationPhase.StoringDresserOutfit => "The outfit could not be submitted. Spring Cleaning stopped, kupo!",
                _ => "The Armoire did not open. Spring Cleaning stopped.",
            });
            return;
        }

        if (DateTime.UtcNow < nextActionAt || !IsActive)
            return;

        try
        {
            switch (Phase)
            {
                case MigrationPhase.Scanning:
                    ScanNextBatch();
                    break;
                case MigrationPhase.Withdrawing:
                    WithdrawNext();
                    break;
                case MigrationPhase.AwaitingWithdrawal:
                    ConfirmWithdrawal();
                    break;
                case MigrationPhase.ClosingDresser:
                    CloseDresser();
                    break;
                case MigrationPhase.MovingToArmoire:
                    MoveTo(armoireObject, MigrationPhase.OpeningArmoire, "Armoire");
                    break;
                case MigrationPhase.OpeningArmoire:
                    OpenArmoire();
                    break;
                case MigrationPhase.Depositing:
                    DepositNext();
                    break;
                case MigrationPhase.AwaitingDeposit:
                    ConfirmDeposit();
                    break;
                case MigrationPhase.ClosingArmoire:
                    CloseArmoire();
                    break;
                case MigrationPhase.MovingToDresser:
                    MoveTo(dresserObject, MigrationPhase.OpeningDresser, "Glamour Dresser");
                    break;
                case MigrationPhase.OpeningDresser:
                    OpenDresser();
                    break;
                case MigrationPhase.ScanningInventoryForDresser:
                    ScanInventoryForDresser();
                    break;
                case MigrationPhase.PreparingDresserOutfit:
                    PrepareDresserOutfit();
                    break;
                case MigrationPhase.StoringDresserOutfit:
                    StoreNextDresserOutfit();
                    break;
                case MigrationPhase.AwaitingDresserOutfit:
                    ConfirmDresserOutfit();
                    break;
                case MigrationPhase.StoringDresserItem:
                    StoreNextDresserItem();
                    break;
                case MigrationPhase.PreparingDresserItem:
                    PrepareDresserItem();
                    break;
                case MigrationPhase.AwaitingDresserItem:
                    ConfirmDresserItem();
                    break;
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "Dresser migration failed during {Phase}", Phase);
            Pause($"Migration paused during {Phase}: {ex.Message}");
        }
    }

    private bool IsDresserUiRestorePending => originalGearsetItemsHidden.HasValue
        || originalDresserCategoryIndex.HasValue;

    private DresserStorageSource ActiveDresserSource => Mode == MigrationMode.ArmouryToDresser
        ? DresserStorageSource.ArmouryChest
        : DresserStorageSource.Inventory;

    private string ActiveDresserSourceName => ActiveDresserSource == DresserStorageSource.ArmouryChest
        ? "Armoury Chest"
        : "inventory";

    private void StartDresserStore(MigrationMode mode)
    {
        var hasFreshOwnership = mode switch
        {
            MigrationMode.InventoryToDresser => HasFreshInventoryDresserOwnership,
            MigrationMode.ArmouryToDresser => HasFreshArmouryDresserOwnership,
            _ => false,
        };
        if (!hasFreshOwnership)
        {
            Pause(mode == MigrationMode.ArmouryToDresser
                ? "Wait for fresh Armoury Chest and Dresser ownership data from Exportaramus."
                : "Wait for fresh inventory and Dresser ownership data from Exportaramus.");
            return;
        }
        if (IsDresserUiRestorePending)
        {
            Status = "Finishing the previous Glamour Creation refresh. Please wait a moment.";
            return;
        }
        var canStart = mode == MigrationMode.ArmouryToDresser
            ? CanStartArmouryDresser
            : CanStartInventoryDresser;
        if (!canStart)
        {
            Pause("Open the Glamour Dresser and wait for its contents to load.");
            return;
        }
        Mode = mode;
        dresserObject = FindCurrentOrNamedObject(DresserName);
        armoireObject = null;
        withdrawnBatch.Clear();
        dresserOutfits.Clear();
        dresserItems.Clear();
        dresserSourceCategoryIndexes.Clear();
        dresserSourceCandidates.Clear();
        dresserSourceCategoryPosition = 0;
        pendingWithdrawal = null;
        pendingDeposit = null;
        pendingDresserOutfit = null;
        pendingDresserItem = null;
        dresserItemOpenDeadline = DateTime.MinValue;
        dresserConfirmationIssued = false;
        nativeUi.CloseInventoryContext();
        originalGearsetItemsHidden = null;
        originalDresserCategoryIndex = null;
        dresserUiRestoreReadyAfter = DateTime.MinValue;
        dresserFilterRefreshReadyAfter = DateTime.MinValue;
        dresserInventoryScanDeadline = DateTime.UtcNow.Add(DresserListRefreshTimeout);
        StoredCount = 0;
        EligibleCount = 0;
        TotalPieceCount = 0;
        Phase = MigrationPhase.ScanningInventoryForDresser;
        Status = scanner.AreDresserInventoryCandidatesReady
            ? $"Checking the {ActiveDresserSourceName} against the Glamour Dresser's eligible-item list."
            : "Waiting for the Glamour Dresser's eligible-item list.";
        nextActionAt = DateTime.UtcNow.AddMilliseconds(configuration.ActionDelayMilliseconds);
        ResetMovementFlags();
    }

    private bool WaitForOwnership(bool ready)
    {
        SupportDiagnostics.Compare("CurrentOwnershipEvidence", 1, ready ? 1 : 0);
        if (ready)
            return true;
        Status = "Waiting for matching current ownership observations from Exportaramus.";
        ShortDelay();
        return false;
    }

    private void ScanNextBatch()
    {
        if (!WaitForOwnership(HasFreshDresserArmoireOwnership))
            return;
        if (!nativeUi.IsDresserOpen || !scanner.IsDresserLoaded)
        {
            Pause("The Glamour Dresser closed before the next batch could be scanned.");
            return;
        }

        var eligible = scanner.RevalidateEligibleItemsForAction(collectionData.LatestSnapshot, dresserArmoirePlanItemIds);
        EligibleCount = eligible.Count;
        if (eligible.Count == 0)
        {
            Phase = MigrationPhase.Complete;
            Status = $"Migration complete. {StoredCount} item{(StoredCount == 1 ? string.Empty : "s")} moved to the Armoire.";
            CurrentItemName = null;
            return;
        }

        var freeSlots = scanner.GetEmptyInventorySlots();
        var usableSlots = Math.Max(0, (int)freeSlots - configuration.ReservedInventorySlots);
        if (usableSlots == 0)
        {
            Pause("There is no free inventory space for the next dresser item.");
            return;
        }

        batchTarget = MigrationBatchPlanner.GetBatchPieceCount(eligible, usableSlots);

        if (batchTarget == 0)
        {
            var first = eligible[0];
            Pause(first.IsOutfit
                ? $"{first.Name} must be restored as one outfit and needs {first.InventorySlotsRequired} free inventory slots."
                : "There is no free inventory space for the next dresser item.");
            return;
        }

        batchWithdrawn = 0;
        Phase = MigrationPhase.Withdrawing;
        Status = $"Withdrawing a batch of {batchTarget} piece{(batchTarget == 1 ? string.Empty : "s")}.";
    }

    private void WithdrawNext()
    {
        if (!WaitForOwnership(HasFreshDresserArmoireOwnership))
            return;
        if (batchWithdrawn >= batchTarget || scanner.GetEmptyInventorySlots() <= configuration.ReservedInventorySlots)
        {
            Phase = MigrationPhase.ClosingDresser;
            Status = "Inventory batch ready. Moving to the Armoire.";
            Delay();
            return;
        }

        var eligible = scanner.RevalidateEligibleItemsForAction(collectionData.LatestSnapshot, dresserArmoirePlanItemIds);
        var remainingCapacity = batchTarget - batchWithdrawn;
        var freeCapacity = Math.Max(0, (int)scanner.GetEmptyInventorySlots() - configuration.ReservedInventorySlots);
        var candidate = MigrationBatchPlanner.FindNextItem(
            eligible,
            Math.Min(remainingCapacity, freeCapacity));
        if (candidate is null)
        {
            Phase = withdrawnBatch.Count == 0 ? MigrationPhase.Complete : MigrationPhase.ClosingDresser;
            Status = withdrawnBatch.Count == 0
                ? $"Migration complete. {StoredCount} items moved to the Armoire."
                : "The remaining eligible list changed; depositing the current batch.";
            return;
        }

        CurrentItemName = candidate.Name;
        var itemCountsBefore = candidate.Components
            .Select(component => component.ItemId)
            .Distinct()
            .ToDictionary(itemId => itemId, scanner.GetInventoryItemCount);
        var emptySlotsBefore = scanner.GetEmptyInventorySlots();
        if (!scanner.TryRestore(candidate))
        {
            Pause($"The game declined to restore {candidate.Name}. Check inventory space or unique-item restrictions.");
            return;
        }

        pendingWithdrawal = new PendingWithdrawal(
            candidate,
            itemCountsBefore,
            emptySlotsBefore,
            DateTime.UtcNow.AddMilliseconds(configuration.ConfirmationTimeoutMilliseconds));
        Phase = MigrationPhase.AwaitingWithdrawal;
        Status = $"Withdrawing {candidate.Name}…";
        Delay();
    }

    private void ConfirmWithdrawal()
    {
        if (pendingWithdrawal is null)
        {
            Pause("Withdrawal confirmation state was lost.");
            return;
        }

        var item = pendingWithdrawal.Item;
        var inventoryIncreased = item.Components
            .GroupBy(component => component.ItemId)
            .All(group => scanner.GetInventoryItemCount(group.Key)
                >= pendingWithdrawal.InventoryCountsBefore.GetValueOrDefault(group.Key) + group.Count());
        var dresserChanged = !scanner.StillPresent(item);
        if (inventoryIncreased && dresserChanged)
        {
            foreach (var component in item.Components)
            {
                withdrawnBatch.Enqueue(new CabinetInventoryItem(
                    component.CabinetRowId,
                    component.ItemId,
                    component.IsHighQuality,
                    component.Name,
                    0));
            }
            batchWithdrawn += item.InventorySlotsRequired;
            pendingWithdrawal = null;
            Phase = MigrationPhase.Withdrawing;
            Status = item.IsOutfit
                ? $"Restored all {item.InventorySlotsRequired} pieces of {item.Name}."
                : $"Batch progress: {batchWithdrawn} / {batchTarget}.";
            Delay();
            return;
        }

        if (DateTime.UtcNow >= pendingWithdrawal.Deadline)
        {
            Pause($"Timed out waiting for the game to confirm withdrawal of {item.Name}.");
            return;
        }

        ShortDelay();
    }

    private void CloseDresser()
    {
        if (nativeUi.IsDresserOpen)
        {
            nativeUi.CloseDresser();
            Status = "Closing the Glamour Dresser…";
            ShortDelay();
            return;
        }

        Phase = MigrationPhase.MovingToArmoire;
        ResetMovementFlags();
        Status = "Moving to the Armoire.";
    }

    private void OpenArmoire()
    {
        if (nativeUi.IsArmoireStoreOpen)
        {
            if (Mode is MigrationMode.InventoryCleanup or MigrationMode.InventoryToArmoire)
            {
                var eligibleInventory = scanner.ScanEligibleInventoryItems(collectionData.LatestSnapshot);
                EligibleCount = eligibleInventory.Count;
                TotalPieceCount = eligibleInventory.Count;
                foreach (var item in eligibleInventory)
                    withdrawnBatch.Enqueue(item);

                if (withdrawnBatch.Count == 0)
                {
                    Phase = Mode == MigrationMode.InventoryToArmoire
                        ? MigrationPhase.Complete
                        : MigrationPhase.ClosingArmoire;
                    Status = Mode == MigrationMode.InventoryToArmoire
                        ? "No eligible inventory outfits need storing in the Armoire."
                        : "No Armoire items remain. Continuing to the Glamour Dresser.";
                    CurrentItemName = null;
                    Delay();
                    return;
                }
            }

            Phase = MigrationPhase.Depositing;
            ResetMovementFlags();
            Status = withdrawnBatch.Count == 0
                ? "Armoire state loaded. Returning to the dresser."
                : $"Depositing {withdrawnBatch.Count} item{(withdrawnBatch.Count == 1 ? string.Empty : "s")}.";
            if (withdrawnBatch.Count == 0)
                Phase = MigrationPhase.ClosingArmoire;
            Delay();
            return;
        }

        if (nativeUi.IsSelectStringOpen)
        {
            if (!menuSelectionIssued || DateTime.UtcNow >= nextInteractionAt)
            {
                menuSelectionIssued = nativeUi.SelectFirstMenuEntry();
                nextInteractionAt = DateTime.UtcNow.Add(InteractionRetryInterval);
                Status = "Opening the Armoire's Store an Item view…";
            }
            ShortDelay();
            return;
        }

        if (!interactionIssued || DateTime.UtcNow >= nextInteractionAt)
        {
            targetManager.Target = armoireObject;
            interactionIssued = NativeUi.Interact(armoireObject?.Address ?? 0);
            if (!interactionIssued)
            {
                Pause("The Armoire could not be interacted with.");
                return;
            }

            interactionAttempts++;
            nextInteractionAt = DateTime.UtcNow.Add(InteractionSettleInterval);
        }

        Status = interactionAttempts <= 1
            ? "Opening the Armoire…"
            : $"Waiting for the Armoire… attempt {interactionAttempts}.";
        ShortDelay();
    }

    private void DepositNext()
    {
        if (!nativeUi.IsArmoireStoreOpen || !scanner.IsCabinetLoaded)
        {
            Pause("The Armoire store view closed before the batch was deposited.");
            return;
        }

        if (withdrawnBatch.Count == 0)
        {
            if (Mode == MigrationMode.InventoryToArmoire)
            {
                Phase = MigrationPhase.Complete;
                Status = $"Armoire storage complete. {StoredCount} item{(StoredCount == 1 ? string.Empty : "s")} stored.";
                CurrentItemName = null;
                return;
            }

            if (Mode == MigrationMode.InventoryCleanup)
            {
                Phase = MigrationPhase.ClosingArmoire;
                Status = "Armoire items stored. Continuing to the Glamour Dresser.";
                Delay();
                return;
            }

            Phase = MigrationPhase.ClosingArmoire;
            Status = "Batch deposited. Returning to the Glamour Dresser.";
            Delay();
            return;
        }

        var item = withdrawnBatch.Peek();
        CurrentItemName = item.Name;
        if (scanner.IsInCabinet(item.CabinetRowId))
        {
            withdrawnBatch.Dequeue();
            Status = $"{item.Name} is already registered in the Armoire; continuing.";
            Delay();
            return;
        }

        var inventoryCount = scanner.GetInventoryItemCount(item.ItemId);
        if (inventoryCount == 0)
        {
            Pause($"The withdrawn item {item.Name} is no longer in the inventory.");
            return;
        }

        if (!WaitForOwnership(CollectionOwnershipPolicy.CanPlanTransfer(collectionData.LatestSnapshot,
                CollectionSourceName.Inventory, CollectionSourceName.Armoire, item.ItemId)))
            return;
        if (!scanner.ScanEligibleInventoryItems(collectionData.LatestSnapshot).Any(candidate => candidate.CabinetRowId == item.CabinetRowId))
        {
            Pause($"{item.Name} is no longer offered by the native Armoire store list.");
            return;
        }
        if (!scanner.TryStore(item.CabinetRowId))
        {
            Pause($"The game declined to store {item.Name} in the Armoire.");
            return;
        }

        pendingDeposit = new PendingDeposit(
            item,
            inventoryCount,
            DateTime.UtcNow.AddMilliseconds(configuration.ConfirmationTimeoutMilliseconds));
        Phase = MigrationPhase.AwaitingDeposit;
        Status = $"Storing {item.Name}…";
        Delay();
    }

    private void ConfirmDeposit()
    {
        if (pendingDeposit is null)
        {
            Pause("Deposit confirmation state was lost.");
            return;
        }

        var item = pendingDeposit.Item;
        var inventoryDecreased = scanner.GetInventoryItemCount(item.ItemId) < pendingDeposit.InventoryCountBefore;
        if (inventoryDecreased && scanner.IsInCabinet(item.CabinetRowId))
        {
            withdrawnBatch.Dequeue();
            StoredCount++;
            pendingDeposit = null;
            Phase = MigrationPhase.Depositing;
            Status = $"Stored {item.Name}. {withdrawnBatch.Count} left in this batch.";
            Delay();
            return;
        }

        if (DateTime.UtcNow >= pendingDeposit.Deadline)
        {
            Pause($"Timed out waiting for the game to confirm storage of {item.Name}.");
            return;
        }

        ShortDelay();
    }

    private void CloseArmoire()
    {
        if (nativeUi.IsArmoireStoreOpen || nativeUi.IsSelectStringOpen)
        {
            nativeUi.CloseArmoire();
            Status = "Closing the Armoire…";
            ShortDelay();
            return;
        }

        Phase = MigrationPhase.MovingToDresser;
        ResetMovementFlags();
        Status = "Returning to the Glamour Dresser.";
    }

    private void OpenDresser()
    {
        if (nativeUi.IsDresserOpen && scanner.IsDresserLoaded)
        {
            if (Mode is MigrationMode.InventoryCleanup or MigrationMode.InventoryToDresser
                or MigrationMode.ArmouryToDresser
                && nativeUi.IsOutfitCreationOpen)
            {
                nativeUi.CloseOutfitCreation();
                Status = "Closing Glamour Creation before inventory storage.";
                ShortDelay();
                return;
            }

            Phase = Mode is MigrationMode.InventoryCleanup or MigrationMode.InventoryToDresser
                or MigrationMode.ArmouryToDresser
                ? MigrationPhase.ScanningInventoryForDresser
                : MigrationPhase.Scanning;
            ResetMovementFlags();
            Status = Mode is MigrationMode.InventoryCleanup or MigrationMode.InventoryToDresser
                or MigrationMode.ArmouryToDresser
                ? $"Checking the {ActiveDresserSourceName} against the Glamour Dresser's eligible-item list."
                : "Rescanning the dresser after the completed batch.";
            Delay();
            return;
        }

        if (!interactionIssued || DateTime.UtcNow >= nextInteractionAt)
        {
            targetManager.Target = dresserObject;
            interactionIssued = NativeUi.Interact(dresserObject?.Address ?? 0);
            if (!interactionIssued)
            {
                Pause("The Glamour Dresser could not be interacted with.");
                return;
            }

            interactionAttempts++;
            nextInteractionAt = DateTime.UtcNow.Add(InteractionSettleInterval);
        }

        Status = interactionAttempts <= 1
            ? "Opening the Glamour Dresser…"
            : $"Waiting for the Glamour Dresser… attempt {interactionAttempts}.";
        ShortDelay();
    }

    private void ScanInventoryForDresser()
    {
        if (!nativeUi.IsDresserOpen || !scanner.IsDresserLoaded)
        {
            Pause("The Glamour Dresser closed before inventory could be scanned.");
            return;
        }
        if (!nativeUi.TryGetDresserCategoryIndex(out var currentCategoryIndex)
            || !nativeUi.TryGetGearsetItemsHidden(out var gearsetItemsHidden))
        {
            Status = "Waiting for the Glamour Creation source and filters to become available.";
            ShortDelay();
            return;
        }

        originalDresserCategoryIndex ??= currentCategoryIndex;
        originalGearsetItemsHidden ??= gearsetItemsHidden;

        var desiredCategoryIndex = NativeUi.InventoryDresserCategory;
        if (ActiveDresserSource == DresserStorageSource.ArmouryChest
            && dresserSourceCategoryIndexes.Count == 0)
        {
            if (!nativeUi.TryGetArmouryChestCategoryIndexes(out var categoryIndexes))
            {
                Status = "Waiting for the Armoury Chest sections to become available.";
                ShortDelay();
                return;
            }

            dresserSourceCategoryIndexes.AddRange(categoryIndexes);
            dresserSourceCategoryPosition = 0;
        }
        if (ActiveDresserSource == DresserStorageSource.ArmouryChest)
        {
            if (dresserSourceCategoryPosition >= dresserSourceCategoryIndexes.Count)
            {
                CompleteDresserStore();
                return;
            }

            desiredCategoryIndex = dresserSourceCategoryIndexes[dresserSourceCategoryPosition];
        }
        if (DateTime.UtcNow < dresserFilterRefreshReadyAfter)
        {
            Status = $"Refreshing eligible {ActiveDresserSourceName} items.";
            ShortDelay();
            return;
        }
        if (!scanner.AreDresserInventoryCandidatesReady)
        {
            if (DateTime.UtcNow >= dresserInventoryScanDeadline)
            {
                Pause("The Glamour Creation item list did not finish refreshing. Close and reopen the Glamour Dresser, then try again.");
                return;
            }

            Status = "Waiting for the Glamour Dresser's eligible-item list.";
            ShortDelay();
            return;
        }
        if (!nativeUi.EnsureDresserCategory(desiredCategoryIndex, out var categoryChanged))
        {
            Status = $"Switching Glamour Creation to the {ActiveDresserSourceName}.";
            ShortDelay();
            return;
        }
        if (categoryChanged)
        {
            dresserFilterRefreshReadyAfter = DateTime.UtcNow.AddMilliseconds(
                Math.Max(configuration.ActionDelayMilliseconds, MinimumFilterRefreshMilliseconds));
            dresserInventoryScanDeadline = DateTime.UtcNow.Add(DresserListRefreshTimeout);
            Status = $"Loading eligible items from the {ActiveDresserSourceName}.";
            ShortDelay();
            return;
        }

        // Inventory and Armoury Chest routes both protect equipment referenced by a gear set.
        const bool shouldHideGearsetItems = true;
        if (gearsetItemsHidden != shouldHideGearsetItems)
        {
            if (!nativeUi.SetGearsetItemsHidden(shouldHideGearsetItems))
            {
                Status = "Waiting for the Glamour Creation gear-set filter.";
                ShortDelay();
                return;
            }

            dresserFilterRefreshReadyAfter = DateTime.UtcNow.AddMilliseconds(
                Math.Max(configuration.ActionDelayMilliseconds, MinimumFilterRefreshMilliseconds));
            dresserInventoryScanDeadline = DateTime.UtcNow.Add(DresserListRefreshTimeout);
            Status = "Excluding items registered to gear sets.";
            ShortDelay();
            return;
        }
        if (!scanner.AreDresserInventoryCandidatesReady)
        {
            if (DateTime.UtcNow >= dresserInventoryScanDeadline)
            {
                Pause("The Glamour Creation item list did not finish refreshing. Close and reopen the Glamour Dresser, then try again.");
                return;
            }

            Status = "Waiting for the Glamour Dresser's eligible-item list.";
            ShortDelay();
            return;
        }

        var scannedCandidates = scanner.ScanEligibleInventoryDresserCandidates(
            collectionData.LatestSnapshot,
            ActiveDresserSource,
            desiredCategoryIndex);
        foreach (var candidate in scannedCandidates)
        {
            if (!dresserSourceCandidates.Any(existing =>
                    existing.Component.InventoryTypeId == candidate.Component.InventoryTypeId
                    && existing.Component.SlotIndex == candidate.Component.SlotIndex
                    && existing.Component.ItemId == candidate.Component.ItemId))
            {
                dresserSourceCandidates.Add(candidate);
            }
        }

        if (ActiveDresserSource == DresserStorageSource.ArmouryChest
            && dresserSourceCategoryPosition + 1 < dresserSourceCategoryIndexes.Count)
        {
            dresserSourceCategoryPosition++;
            dresserInventoryScanDeadline = DateTime.UtcNow.Add(DresserListRefreshTimeout);
            Status = $"Checking Armoury Chest section {dresserSourceCategoryPosition + 1} of {dresserSourceCategoryIndexes.Count}.";
            Delay();
            return;
        }

        var allOutfits = scanner.PlanEligibleInventoryDresserOutfits(
                dresserSourceCandidates,
                collectionData.LatestSnapshot,
                configuration.OnlyMoveCompleteOutfits)
            .ToArray();
        var allItems = DresserScanner.PlanEligibleInventoryDresserItems(
                dresserSourceCandidates.Where(candidate => scanner.CanStoreDresserItem(
                    collectionData.LatestSnapshot, candidate.Component.ItemId, ActiveDresserSource)).ToArray())
            .ToArray();
        var allPieceCount = allOutfits.Sum(outfit => outfit.Components.Count) + allItems.Length;
        IsDresserPrismCountReady = scanner.TryGetGlamourPrismCount(out var prismCount);
        DresserPrismsHeld = prismCount;
        DresserPrismsRequired = allPieceCount;
        var budgetPlan = DresserPrismBudgetPlanner.Plan(
            allOutfits,
            allItems,
            uint.MaxValue);

        dresserOutfits.Clear();
        foreach (var outfit in budgetPlan.Outfits)
            dresserOutfits.Enqueue(outfit);
        dresserItems.Clear();
        foreach (var item in budgetPlan.Items)
            dresserItems.Enqueue(item);

        log.Information(
            "{Source}-to-Dresser plan contains {OutfitCount} outfits, {StandaloneCount} standalone items, and {PieceCount} pieces: {Outfits}",
            ActiveDresserSourceName,
            dresserOutfits.Count,
            dresserItems.Count,
            budgetPlan.SelectedPieces,
            string.Join(", ", dresserOutfits.Select(outfit => outfit.Name)
                .Concat(dresserItems.Select(item => item.Name))));

        var dresserPieceCount = budgetPlan.SelectedPieces;
        TotalPieceCount += dresserPieceCount;
        if (dresserOutfits.Count == 0 && dresserItems.Count == 0)
        {
            CompleteDresserStore();
            return;
        }

        Phase = dresserOutfits.Count > 0
            ? MigrationPhase.StoringDresserOutfit
            : MigrationPhase.StoringDresserItem;
        Status = $"Storing {dresserPieceCount} remaining item"
            + (dresserPieceCount == 1 ? string.Empty : "s")
            + " in the Glamour Dresser.";
        Delay();
    }

    private bool CheckDresserPrismsBeforeAction()
    {
        _ = scanner.TryGetGlamourPrismCount(out var held);
        DresserPrismsHeld = held;
        if (SupportDiagnostics.Compare("GlamourPrismsAvailable", 1, held > 0 ? 1 : 0))
            return true;

        // Zero is deliberately not trusted for preflight budgeting, but must never
        // authorize another native storage attempt. Pause leaves the game under user control.
        Pause("I've stopped storing items because no Glamour Prisms are available, kupo!");
        return false;
    }

    private void StoreNextDresserOutfit()
    {
        if (!CheckDresserPrismsBeforeAction())
            return;
        if (!nativeUi.IsDresserOpen || !scanner.IsDresserLoaded)
        {
            Pause("The Glamour Dresser closed before the next outfit was stored.");
            return;
        }
        if (dresserOutfits.Count == 0)
        {
            ContinueWithStandaloneDresserItems();
            return;
        }

        var outfit = dresserOutfits.Peek();
        if (!WaitForOwnership(ActiveDresserSource == DresserStorageSource.Inventory
                ? HasFreshInventoryDresserOwnership : HasFreshArmouryDresserOwnership))
            return;
        if (!scanner.CanStoreOutfit(collectionData.LatestSnapshot, outfit, ActiveDresserSource))
        {
            dresserOutfits.Dequeue();
            nativeUi.CloseOutfitCreation();
            Status = $"Skipped {outfit.Name}: its selected pieces changed or are already represented.";
            Delay();
            return;
        }
        if (DateTime.UtcNow < dresserReadyAfter)
        {
            Status = "Waiting for the Glamour Dresser to become ready for the next outfit.";
            ShortDelay();
            return;
        }
        if (Mode == MigrationMode.ArmouryToDresser
            && !EnsureArmouryCategoryFor(outfit))
        {
            return;
        }

        CurrentItemName = outfit.Name;
        if (!nativeUi.IsOutfitCreationOpen)
        {
            var opened = nativeUi.OpenOutfitCreation(outfit);

            Phase = MigrationPhase.PreparingDresserOutfit;
            Status = opened
                ? $"Opening Glamour Creation for {outfit.Name}…"
                : $"Waiting for Glamour Creation to reset before opening {outfit.Name}.";
            dresserPreparationDeadline = DateTime.UtcNow.Add(NativeOperationTimeout);
            nextDresserOpenAttemptAt = DateTime.UtcNow.Add(DresserOpenRetryInterval);
            dresserOpenAttempts = 1;
            Delay();
            return;
        }

        if (!nativeUi.IsOutfitCreationFor(outfit))
        {
            nativeUi.CloseOutfitCreation();
            dresserReadyAfter = DateTime.UtcNow.Add(DresserOpenRetryInterval);
            Status = "Closing the previous Glamour Creation window before continuing.";
            ShortDelay();
            return;
        }

        var countsBefore = outfit.Components
            .Select(component => component.ItemId)
            .Distinct()
            .ToDictionary(itemId => itemId, itemId => scanner.GetDresserSourceItemCount(itemId, ActiveDresserSource));

        var pending = new PendingDresserOutfit(
            outfit,
            countsBefore,
            DateTime.UtcNow.AddMilliseconds(
                Math.Max(configuration.ConfirmationTimeoutMilliseconds, MinimumNativeConfirmationMilliseconds)));
        if (!nativeUi.TrySelectOutfitPieces(outfit, out var selectionStatus))
        {
            Status = selectionStatus;
            ShortDelay();
            return;
        }
        if (!nativeUi.SubmitOutfitCreation(out var submissionStatus))
        {
            Status = submissionStatus;
            ShortDelay();
            return;
        }

        pendingDresserOutfit = pending;
        dresserConfirmationIssued = false;
        Phase = MigrationPhase.AwaitingDresserOutfit;
        Status = submissionStatus;
        log.Information("Submitted Store as Glamour for {Outfit}", outfit.Name);
        Delay();
    }

    private void PrepareDresserOutfit()
    {
        if (!CheckDresserPrismsBeforeAction())
            return;
        if (dresserOutfits.Count == 0)
        {
            Pause("The pending Dresser outfit was lost.");
            return;
        }

        var outfit = dresserOutfits.Peek();
        if (DateTime.UtcNow >= dresserPreparationDeadline)
        {
            Pause($"The game did not finish preparing {outfit.Name}. Spring Cleaning stopped, kupo!");
            return;
        }
        if (!WaitForOwnership(scanner.CanStoreOutfit(collectionData.LatestSnapshot, outfit, ActiveDresserSource)))
        {
            if (DateTime.UtcNow >= dresserPreparationDeadline)
                Pause($"Could not prepare {outfit.Name}: its provider evidence or selected copy changed.");
            return;
        }
        if (scanner.TryGetGlamourPrismCount(out var prismCount))
        {
            DresserPrismsHeld = prismCount;
            IsDresserPrismCountReady = true;
        }
        // When the selected set already exists in the Dresser, the game asks
        // whether to add to it before Glamour Creation itself opens.
        if (outfit.ExistingPrismBoxIndex.HasValue && nativeUi.IsExistingOutfitMergePromptOpen)
        {
            if (nativeUi.ConfirmExistingOutfitMerge(out var mergeStatus))
                log.Information("Confirmed opening existing outfit glamour {Outfit}", outfit.Name);

            Status = mergeStatus;
            ShortDelay();
            return;
        }

        if (!nativeUi.IsOutfitCreationOpen)
        {
            if (DateTime.UtcNow >= dresserPreparationDeadline)
            {
                Pause($"The game did not open Glamour Creation for {outfit.Name}.");
                return;
            }
            if (Mode == MigrationMode.ArmouryToDresser
                && !EnsureArmouryCategoryFor(outfit))
            {
                return;
            }

            if (DateTime.UtcNow >= nextDresserOpenAttemptAt)
            {
                dresserOpenAttempts++;
                nativeUi.OpenOutfitCreation(outfit);
                nextDresserOpenAttemptAt = DateTime.UtcNow.Add(DresserOpenRetryInterval);
            }

            Status = dresserOpenAttempts <= 1
                ? $"Waiting for Glamour Creation to open for {outfit.Name}."
                : $"Waiting for Glamour Creation to open for {outfit.Name}; attempt {dresserOpenAttempts}.";
            ShortDelay();
            return;
        }

        if (!nativeUi.TrySelectOutfitPieces(outfit, out var preparationStatus))
        {
            if (DateTime.UtcNow >= dresserPreparationDeadline)
            {
                Pause($"Could not prepare {outfit.Name}: {preparationStatus}");
                return;
            }

            Status = preparationStatus;
            ShortDelay();
            return;
        }

        Phase = MigrationPhase.StoringDresserOutfit;
        Status = preparationStatus;
        Delay();
    }

    private void ConfirmDresserOutfit()
    {
        if (pendingDresserOutfit is null)
        {
            Pause("Dresser confirmation state was lost.");
            return;
        }

        var outfit = pendingDresserOutfit.Outfit;
        var inventoryDecreased = outfit.Components
            .GroupBy(component => component.ItemId)
            .All(group => scanner.GetDresserSourceItemCount(group.Key, ActiveDresserSource) + group.Count()
                <= pendingDresserOutfit.InventoryCountsBefore.GetValueOrDefault(group.Key));
        var stored = outfit.Components.All(component => scanner.IsAnyQualityStoredInDresser(component.ItemId));
        // Retain completion evidence on every wait, including after native dialogs close.
        // Earlier selection/button checks alone cannot prove that the game stored anything.
        nativeUi.Diagnostics.Compare("OutfitInventoryDecreased", 1, inventoryDecreased ? 1 : 0);
        nativeUi.Diagnostics.Compare("OutfitAppearancesStored", 1, stored ? 1 : 0);
        nativeUi.Diagnostics.Compare("OutfitConfirmationIssued", 1, dresserConfirmationIssued ? 1 : 0);
        if (inventoryDecreased && stored)
        {
            nativeUi.CloseOutfitCreation();
            dresserOutfits.Dequeue();
            StoredCount += outfit.Components.Count;
            pendingDresserOutfit = null;
            dresserConfirmationIssued = false;
            dresserReadyAfter = DateTime.UtcNow.Add(DresserReadyInterval);
            dresserInventoryScanDeadline = DateTime.UtcNow.Add(NativeOperationTimeout);
            var remaining = dresserOutfits.Sum(queued => queued.Components.Count) + dresserItems.Count;
            if (remaining == 0)
            {
                CompleteDresserStore();
                return;
            }

            Phase = dresserOutfits.Count > 0
                ? MigrationPhase.StoringDresserOutfit
                : MigrationPhase.StoringDresserItem;
            Status = $"Stored {outfit.Name}. {remaining} item{(remaining == 1 ? string.Empty : "s")} left.";
            Delay();
            return;
        }

        // A still-visible prompt must not extend the pending operation indefinitely.
        // Completion is checked first so a confirmed final prism may settle normally.
        if (DateTime.UtcNow >= pendingDresserOutfit.Deadline)
        {
            Pause($"The game did not confirm storage of {outfit.Name}. Spring Cleaning stopped, kupo!");
            return;
        }

        if (!dresserConfirmationIssued
            && (!scanner.CanStoreOutfit(collectionData.LatestSnapshot, outfit, ActiveDresserSource)
                || !nativeUi.IsOutfitSelectionExact(outfit)))
        {
            if (DateTime.UtcNow >= pendingDresserOutfit.Deadline)
                Pause("The selected outfit changed before storage was confirmed.");
            else
                WaitForOwnership(false);
            return;
        }

        if (!dresserConfirmationIssued && !CheckDresserPrismsBeforeAction())
            return;

        // Extending a Dresser outfit inserts a standard Yes/No prompt before
        // the normal storage confirmation. Handle it first; both addons may
        // overlap briefly while the native UI transitions.
        if (outfit.ExistingPrismBoxIndex.HasValue && nativeUi.IsExistingOutfitMergePromptOpen)
        {
            if (nativeUi.ConfirmExistingOutfitMerge(out var mergeStatus))
                log.Information("Confirmed adding pieces to existing outfit glamour {Outfit}", outfit.Name);

            Status = mergeStatus;
            Delay();
            return;
        }

        if (nativeUi.IsOutfitConfirmationOpen)
        {
            var confirmationStatus = "Waiting for the outfit storage confirmation.";
            if (!dresserConfirmationIssued)
            {
                dresserConfirmationIssued = nativeUi.ConfirmOutfitStorage(
                    outfit.ExistingPrismBoxIndex.HasValue,
                    out confirmationStatus);
                if (dresserConfirmationIssued)
                    log.Information("Confirmed Store as Outfit Glamour for {Outfit}", outfit.Name);
            }

            Status = dresserConfirmationIssued
                ? "Waiting for the game to finish storing the outfit."
                : confirmationStatus;
            Delay();
            return;
        }

        ShortDelay();
    }

    private void ContinueWithStandaloneDresserItems()
    {
        if (dresserItems.Count > 0)
        {
            Phase = MigrationPhase.StoringDresserItem;
            Status = $"Storing {dresserItems.Count} standalone item{(dresserItems.Count == 1 ? string.Empty : "s")} in the Glamour Dresser.";
            Delay();
            return;
        }

        CompleteDresserStore();
    }

    private void StoreNextDresserItem()
    {
        if (!CheckDresserPrismsBeforeAction())
            return;
        if (!nativeUi.IsDresserOpen || !scanner.IsDresserLoaded)
        {
            Pause("The Glamour Dresser closed before the next item was stored.");
            return;
        }
        if (dresserItems.Count == 0)
        {
            ContinueWithStandaloneDresserItems();
            return;
        }
        if (DateTime.UtcNow < dresserReadyAfter)
        {
            Status = "Waiting for the Glamour Dresser to become ready for the next item.";
            ShortDelay();
            return;
        }
        var item = dresserItems.Peek();
        if (!WaitForOwnership(ActiveDresserSource == DresserStorageSource.Inventory
                ? HasFreshInventoryDresserOwnership : HasFreshArmouryDresserOwnership))
            return;
        if (!scanner.CanStoreDresserCopy(collectionData.LatestSnapshot, item, ActiveDresserSource))
        {
            dresserItems.Dequeue();
            Status = $"Skipped {item.Name}: it changed or is already represented.";
            Delay();
            return;
        }
        if (Mode == MigrationMode.ArmouryToDresser
            && !EnsureArmouryCategoryFor(item))
        {
            return;
        }

        CurrentItemName = item.Name;
        if (dresserItemOpenDeadline == DateTime.MinValue)
            dresserItemOpenDeadline = DateTime.UtcNow.Add(NativeOperationTimeout);
        var countBefore = scanner.GetDresserSourceItemCount(item.ItemId, ActiveDresserSource);
        if (countBefore == 0)
        {
            Pause($"{item.Name} is no longer in the {ActiveDresserSourceName}.");
            return;
        }

        var opened = nativeUi.OpenDresserStorage(item);
        if (!opened)
        {
            if (DateTime.UtcNow < dresserItemOpenDeadline)
            {
                Status = $"Waiting for {item.Name} to appear in the Glamour Creation list.";
                ShortDelay();
                return;
            }

            Pause($"The game could not begin Glamour Dresser storage for {item.Name}.");
            return;
        }

        dresserItemOpenDeadline = DateTime.MinValue;
        pendingDresserItem = new PendingDresserItem(
            item,
            countBefore,
            DateTime.UtcNow.AddMilliseconds(
                Math.Max(configuration.ConfirmationTimeoutMilliseconds, MinimumNativeConfirmationMilliseconds)));
        dresserItemConfirmationIssued = false;
        Phase = MigrationPhase.PreparingDresserItem;
        Status = $"Opening Glamour Dresser confirmation for {item.Name}.";
        Delay();
    }

    private void PrepareDresserItem()
    {
        if (pendingDresserItem is null)
        {
            Pause("Standalone Dresser item state was lost.");
            return;
        }

        if (TryCompleteDresserItem())
            return;
        if (DateTime.UtcNow >= pendingDresserItem.Deadline)
        {
            Pause($"The game did not open storage confirmation for {pendingDresserItem.Item.Name}.");
            return;
        }
        if (!CheckDresserPrismsBeforeAction())
            return;

        if (!WaitForOwnership(scanner.CanStoreDresserCopy(collectionData.LatestSnapshot,
                pendingDresserItem.Item, ActiveDresserSource)))
        {
            if (DateTime.UtcNow >= pendingDresserItem.Deadline)
                Pause("The selected item changed before storage was confirmed.");
            return;
        }
        if (nativeUi.IsDresserItemConfirmationOpen)
        {
            dresserItemConfirmationIssued = nativeUi.ConfirmDresserItemStorage();
            Phase = MigrationPhase.AwaitingDresserItem;
            Status = dresserItemConfirmationIssued
                ? $"Storing {pendingDresserItem.Item.Name}…"
                : "Waiting for the Glamour Dresser confirmation.";
            Delay();
            return;
        }

        Status = "Waiting for the Glamour Dresser storage confirmation.";
        ShortDelay();
    }

    private void ConfirmDresserItem()
    {
        if (pendingDresserItem is null)
        {
            Pause("Standalone Dresser confirmation state was lost.");
            return;
        }

        if (TryCompleteDresserItem())
            return;

        if (DateTime.UtcNow >= pendingDresserItem.Deadline)
        {
            Pause($"The game did not confirm storage of {pendingDresserItem.Item.Name}.");
            return;
        }

        if (nativeUi.IsDresserItemConfirmationOpen && !dresserItemConfirmationIssued
            && scanner.CanStoreDresserCopy(collectionData.LatestSnapshot, pendingDresserItem.Item, ActiveDresserSource))
        {
            if (!CheckDresserPrismsBeforeAction())
                return;
            dresserItemConfirmationIssued = nativeUi.ConfirmDresserItemStorage();
        }

        Status = $"Waiting for the game to finish storing {pendingDresserItem.Item.Name}.";
        ShortDelay();
    }

    private bool TryCompleteDresserItem()
    {
        if (pendingDresserItem is null)
            return false;

        var item = pendingDresserItem.Item;
        var inventoryDecreased = scanner.GetDresserSourceItemCount(item.ItemId, ActiveDresserSource)
            < pendingDresserItem.InventoryCountBefore;
        var stored = scanner.IsAnyQualityStoredInDresser(item.ItemId);
        if (!inventoryDecreased || !stored)
            return false;

        dresserItems.Dequeue();
        StoredCount++;
        pendingDresserItem = null;
        dresserItemConfirmationIssued = false;
        dresserItemOpenDeadline = DateTime.MinValue;
        dresserReadyAfter = DateTime.UtcNow.Add(DresserReadyInterval);
        dresserInventoryScanDeadline = DateTime.UtcNow.Add(NativeOperationTimeout);
        CurrentItemName = null;
        if (dresserItems.Count == 0)
        {
            CompleteDresserStore();
            return true;
        }

        Phase = MigrationPhase.StoringDresserItem;
        Status = $"Stored {item.Name}. {dresserItems.Count} item{(dresserItems.Count == 1 ? string.Empty : "s")} left.";
        Delay();
        return true;
    }

    private bool EnsureArmouryCategoryFor(InventoryDresserOutfit outfit)
    {
        if (outfit.Components.Count == 0)
        {
            Pause($"No Armoury Chest pieces remained for {outfit.Name}.");
            return false;
        }

        return EnsureArmouryCategory(outfit.Components[0].SourceCategoryIndex, outfit.Name);
    }

    private bool EnsureArmouryCategoryFor(InventoryDresserItem item) =>
        EnsureArmouryCategory(item.SourceCategoryIndex, item.Name);

    private bool EnsureArmouryCategory(int sourceCategoryIndex, string itemName)
    {
        if (Mode != MigrationMode.ArmouryToDresser)
            return true;

        if (DateTime.UtcNow < dresserFilterRefreshReadyAfter)
        {
            Status = $"Loading the Armoury Chest section for {itemName}.";
            ShortDelay();
            return false;
        }

        if (!nativeUi.TryGetDresserCategoryIndex(out var currentCategoryIndex))
        {
            Status = "Waiting for the current Armoury Chest section.";
            ShortDelay();
            return false;
        }

        if (currentCategoryIndex != sourceCategoryIndex)
        {
            if (!WaitForArmouryCandidatesToRefresh(currentCategoryIndex))
                return false;

            if (!nativeUi.EnsureDresserCategory(sourceCategoryIndex, out var changed))
            {
                Status = $"Switching to the Armoury Chest section containing {itemName}.";
                ShortDelay();
                return false;
            }

            if (changed)
            {
                dresserFilterRefreshReadyAfter = DateTime.UtcNow.AddMilliseconds(
                    Math.Max(configuration.ActionDelayMilliseconds, MinimumFilterRefreshMilliseconds));
                dresserInventoryScanDeadline = DateTime.UtcNow.Add(DresserListRefreshTimeout);
                Status = $"Loading the Armoury Chest section containing {itemName}.";
                ShortDelay();
                return false;
            }
        }

        return WaitForArmouryCandidatesToRefresh(sourceCategoryIndex);
    }

    private bool WaitForArmouryCandidatesToRefresh(int? expectedCategoryIndex = null)
    {
        if (expectedCategoryIndex.HasValue
            && (!nativeUi.TryGetDresserCategoryIndex(out var currentCategoryIndex)
                || currentCategoryIndex != expectedCategoryIndex.Value))
        {
            Status = "Waiting for the requested Armoury Chest section.";
            ShortDelay();
            return false;
        }
        if (nativeUi.IsDresserItemConfirmationOpen
            || nativeUi.IsOutfitConfirmationOpen
            || nativeUi.IsExistingOutfitMergePromptOpen)
        {
            Status = "Waiting for the previous Glamour Dresser confirmation to close.";
            ShortDelay();
            return false;
        }
        if (scanner.AreDresserInventoryCandidatesReady)
            return true;

        if (dresserInventoryScanDeadline == DateTime.MinValue)
            dresserInventoryScanDeadline = DateTime.UtcNow.Add(NativeOperationTimeout);
        if (DateTime.UtcNow >= dresserInventoryScanDeadline)
        {
            Pause("The Armoury Chest item list did not finish refreshing.");
            return false;
        }

        Status = "Waiting for the Armoury Chest item list to finish refreshing.";
        ShortDelay();
        return false;
    }

    private void CompleteDresserStore()
    {
        var restoreAfter = DateTime.UtcNow.Add(UiRestoreInterval);
        if (dresserReadyAfter > restoreAfter)
            restoreAfter = dresserReadyAfter;
        if (dresserFilterRefreshReadyAfter > restoreAfter)
            restoreAfter = dresserFilterRefreshReadyAfter;
        dresserUiRestoreReadyAfter = restoreAfter;
        Phase = MigrationPhase.Complete;
        Status = configuration.OnlyMoveCompleteOutfits && StoredCount == 0
            ? $"No complete outfits with eligible pieces were found in your {ActiveDresserSourceName}."
            : $"{ActiveDresserSourceName} cleanup complete. {StoredCount} item{(StoredCount == 1 ? string.Empty : "s")} stored.";
        CurrentItemName = null;
    }

    private void MoveTo(IGameObject? destination, MigrationPhase arrivalPhase, string label)
    {
        if (destination is null || !destination.IsValid())
        {
            Pause($"The {label} furnishing is no longer available.");
            return;
        }

        var player = objectTable.LocalPlayer;
        if (player is null)
        {
            Pause("The local player object is unavailable.");
            return;
        }

        var distance = Vector3.Distance(player.Position, destination.Position);
        if (distance <= configuration.InteractionDistance)
        {
            navmesh.Stop();
            Phase = arrivalPhase;
            ResetMovementFlags();
            Status = $"Opening the {label}.";
            ShortDelay();
            return;
        }

        if (!navmesh.IsAvailable())
        {
            Pause($"vnavmesh is unavailable and the {label} is {distance:F1} yalms away.");
            return;
        }

        if (!navigationIssued)
        {
            navigationIssued = navmesh.MoveTo(destination.Position, configuration.InteractionDistance - 0.5f);
            if (!navigationIssued)
            {
                Pause($"vnavmesh could not start a path to the {label}.");
                return;
            }
        }
        else if (!navmesh.IsMoving() && distance > configuration.InteractionDistance)
        {
            Pause($"Movement to the {label} stopped before reaching interaction range.");
            return;
        }

        Status = $"Moving to the {label} ({distance:F1} yalms).";
        ShortDelay();
    }

    private IGameObject? FindCurrentOrNamedObject(string name)
    {
        var target = targetManager.Target;
        if (target is not null && target.IsValid()
            && IsStation(target, name))
        {
            return target;
        }

        return FindNamedObject(name);
    }

    private IGameObject? FindNamedObject(string name) => objectTable
        .Where(gameObject => gameObject is not null
            && gameObject.IsValid()
            && IsStation(gameObject, name))
        .OrderBy(gameObject => objectTable.LocalPlayer is null
            ? float.MaxValue
            : Vector3.Distance(objectTable.LocalPlayer.Position, gameObject.Position))
        .FirstOrDefault();

    private bool IsStation(IGameObject gameObject, string stationName)
    {
        var ids = stationName == DresserName ? dresserStationIds : armoireStationIds;
        return StationIdentityPolicy.Matches(
            ids,
            gameObject.BaseId,
            gameObject.Name.TextValue,
            stationName);
    }

    private static void RememberStation(HashSet<uint> ids, IGameObject gameObject)
    {
        if (gameObject.BaseId != 0 && ids.Count < MaximumRememberedStationIds)
            ids.Add(gameObject.BaseId);
    }

    private static HashSet<uint> ResolveHousingStationIds(
        IDataManager dataManager,
        string englishName)
    {
        var itemIds = dataManager.GetExcelSheet<Item>(ClientLanguage.English)
            .Where(item => item.Name.ToString().Equals(englishName, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.RowId)
            .ToHashSet();
        var ids = new HashSet<uint>(itemIds.Take(MaximumRememberedStationIds));
        foreach (var furniture in dataManager.GetExcelSheet<HousingFurniture>())
        {
            if (ids.Count >= MaximumRememberedStationIds)
                break;
            if (itemIds.Contains(furniture.Item.RowId))
                ids.Add(furniture.RowId);
        }

        return ids;
    }

    private void Pause(string reason)
    {
        SupportDiagnostics.RetainFailure(CaptureDiagnostics(), collectionData.Snapshot, reason);
        navmesh.Stop();
        RestoreDresserUiStateIfNeeded();
        Phase = MigrationPhase.Paused;
        Status = reason;
        CurrentItemName = null;
        pendingWithdrawal = null;
        pendingDeposit = null;
        pendingDresserOutfit = null;
        pendingDresserItem = null;
        dresserItemOpenDeadline = DateTime.MinValue;
        dresserConfirmationIssued = false;
        dresserItemConfirmationIssued = false;
        log.Warning("Dresser migration paused: {Reason}", reason);
    }

    private void RestoreDresserUiStateIfNeeded()
    {
        if (!IsDresserUiRestorePending)
        {
            dresserFilterRefreshReadyAfter = DateTime.MinValue;
            dresserInventoryScanDeadline = DateTime.MinValue;
            dresserItemOpenDeadline = DateTime.MinValue;
            return;
        }
        if (DateTime.UtcNow < dresserUiRestoreReadyAfter)
            return;
        if (!nativeUi.IsDresserOpen || !nativeUi.IsGlamourCreationOpen)
        {
            // Closing the native UI makes its prior filter/category snapshot
            // irrelevant. Do not leave every Spring Cleaning action disabled
            // waiting for a window the user intentionally closed.
            originalGearsetItemsHidden = null;
            originalDresserCategoryIndex = null;
            dresserFilterRefreshReadyAfter = DateTime.MinValue;
            dresserInventoryScanDeadline = DateTime.MinValue;
            dresserItemOpenDeadline = DateTime.MinValue;
            return;
        }
        if (nativeUi.IsDresserItemConfirmationOpen
            || nativeUi.IsOutfitCreationOpen
            || nativeUi.IsOutfitConfirmationOpen
            || nativeUi.IsExistingOutfitMergePromptOpen
            || !scanner.AreDresserInventoryCandidatesReady)
        {
            return;
        }

        if (originalGearsetItemsHidden is bool originalHidden)
        {
            if (!nativeUi.TryGetGearsetItemsHidden(out var currentHidden))
                return;
            if (currentHidden != originalHidden)
            {
                if (nativeUi.SetGearsetItemsHidden(originalHidden))
                    dresserUiRestoreReadyAfter = DateTime.UtcNow.Add(UiRestoreInterval);
                return;
            }

            originalGearsetItemsHidden = null;
        }

        if (originalDresserCategoryIndex is int originalCategory)
        {
            if (!nativeUi.EnsureDresserCategory(originalCategory, out var changed))
                return;
            if (changed)
            {
                dresserUiRestoreReadyAfter = DateTime.UtcNow.Add(UiRestoreInterval);
                return;
            }

            originalDresserCategoryIndex = null;
        }

        dresserFilterRefreshReadyAfter = DateTime.MinValue;
        dresserInventoryScanDeadline = DateTime.MinValue;
        dresserItemOpenDeadline = DateTime.MinValue;
    }

    private void ResetMovementFlags()
    {
        nativePhaseDeadline.Reset();
        navigationIssued = false;
        interactionIssued = false;
        menuSelectionIssued = false;
        interactionAttempts = 0;
        nextInteractionAt = DateTime.MinValue;
    }

    private void Delay() =>
        nextActionAt = DateTime.UtcNow.AddMilliseconds(configuration.ActionDelayMilliseconds);

    private void ShortDelay() =>
        nextActionAt = DateTime.UtcNow.Add(ShortActionInterval);
}
