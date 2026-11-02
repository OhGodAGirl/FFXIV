namespace SpringCleaning;

internal static class ExistingOutfitConfirmationPolicy
{
    public static bool Matches(string? prompt, string? expected) =>
        !string.IsNullOrWhiteSpace(prompt)
        && !string.IsNullOrWhiteSpace(expected)
        && string.Equals(Normalize(prompt), Normalize(expected), StringComparison.Ordinal);

    private static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
