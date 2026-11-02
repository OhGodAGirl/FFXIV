namespace EZDelivery.Features.Shopping;

/// <summary>Normalizes native prompt text for exact expected-text comparison.</summary>
public static class ShopPrompt
{
    // Native text nodes and sheet text can encode line breaks differently.
    // Preserve every word and punctuation mark; normalize only whitespace.
    /// <summary>Removes presentation whitespace differences from a prompt.</summary>
    public static string Normalize(string value)
        => string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Compares normalized actual and expected confirmation prompts.</summary>
    public static bool Matches(string observed, string expected)
        => expected.Length > 0 && Normalize(observed) == Normalize(expected);
}
