using System;

namespace CollectionExporter;

internal static class FfxivCollectProfile
{
    internal const string Host = "ffxivcollect.com";

    internal static string GetCharacterUrl(ulong characterId) =>
        $"https://{Host}/characters/{characterId}";

    internal static bool TryParseCharacterId(string input, out ulong characterId)
    {
        var value = input.Trim().TrimEnd('/');
        if (ulong.TryParse(value, out characterId))
            return characterId > 0;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase)
            || uri.Segments.Length != 3
            || !string.Equals(uri.Segments[1].Trim('/'), "characters", StringComparison.OrdinalIgnoreCase)
            || !ulong.TryParse(uri.Segments[2].Trim('/'), out characterId))
        {
            return false;
        }

        return characterId > 0;
    }
}
