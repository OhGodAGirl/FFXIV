namespace SpringCleaning;

internal static class DiscardConfirmationPolicy
{
    public static bool CanControlOwnedPrompt(bool requestIssued) => requestIssued;

    public static bool MatchesExpectedItem(string prompt, string itemName) =>
        !string.IsNullOrWhiteSpace(prompt)
        && !string.IsNullOrWhiteSpace(itemName)
        && prompt.Contains(itemName, StringComparison.OrdinalIgnoreCase);
}
