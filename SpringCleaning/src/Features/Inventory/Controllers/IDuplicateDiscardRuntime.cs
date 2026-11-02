namespace SpringCleaning;

internal interface IDuplicateDiscardRuntime
{
    // Null means the source cannot currently be observed, never proof of removal.
    bool? IsInventoryItemPresent(InventoryDuplicateItem candidate);

    bool IsDresserOpen { get; }

    bool IsDiscardConfirmationOpen { get; }

    bool IsOccupied { get; }

    void CloseDresser();

    bool CanSafelyOpenDiscardPrompt(InventoryDuplicateItem candidate);

    bool RequestDiscard(InventoryDuplicateItem candidate);

    bool ConfirmDiscardConfirmation(InventoryDuplicateItem candidate, bool requestIssued);

    void CancelDiscardConfirmation(InventoryDuplicateItem candidate, bool requestIssued);

    bool OpenInventoryContext(InventoryDuplicateItem candidate);
}
