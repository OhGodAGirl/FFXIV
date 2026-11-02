namespace SpringCleaning;

internal enum MigrationPhase
{
    Idle,
    Scanning,
    Withdrawing,
    AwaitingWithdrawal,
    ClosingDresser,
    MovingToArmoire,
    OpeningArmoire,
    Depositing,
    AwaitingDeposit,
    ClosingArmoire,
    MovingToDresser,
    OpeningDresser,
    ScanningInventoryForDresser,
    PreparingDresserOutfit,
    StoringDresserOutfit,
    AwaitingDresserOutfit,
    PreparingDresserItem,
    StoringDresserItem,
    AwaitingDresserItem,
    Complete,
    Paused,
    Cancelled,
}
