namespace EZDelivery.Bootstrap;

/// <summary>Pure admission checks for the reviewed game and native structure binaries.</summary>
public static class NativeBuildPolicy
{
    /// <summary>SHA-256 of the reviewed game executable.</summary>
    public const string GameSha256 = "B38840761E2677128B6BEC1CEAEE3F2870CD84E1D38AB7958216373058383780";
    /// <summary>SHA-256 of the reviewed ClientStructs assembly.</summary>
    public const string StructsSha256 = "A47750CE4BE1910C8783231F398559BCCE43E4A6AE8D255351B3BBA8247D6F9D";

    /// <summary>Recognizes the supported game executable name.</summary>
    public static bool IsGameProcess(string? path)
        => path is not null && Path.GetFileName(path).Equals("ffxiv_dx11.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>Requires both current fingerprints to match the reviewed pair.</summary>
    public static bool Matches(string gameHash, string structsHash)
        => gameHash == GameSha256 && structsHash == StructsSha256;
}
