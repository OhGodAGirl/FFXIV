using System.Security.Cryptography;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace EZDelivery.Bootstrap;

/// <summary>Offline-reviewed native layouts are valid only for these exact input binaries.</summary>
public sealed record NativeBuild(bool Supported, string Detail)
{
    /// <summary>Fingerprint of the reviewed game executable.</summary>
    public const string GameSha256 = NativeBuildPolicy.GameSha256;
    /// <summary>Fingerprint of the reviewed ClientStructs assembly.</summary>
    public const string StructsSha256 = NativeBuildPolicy.StructsSha256;
    /// <summary>Reads current binary fingerprints and returns native admission status.</summary>
    public static NativeBuild Inspect()
    {
        try
        {
            var game = Environment.ProcessPath;
            var structs = typeof(AddonGrandCompanySupplyList).Assembly.Location;
            if (!NativeBuildPolicy.IsGameProcess(game))
                return new(false, "FFXIV executable identity unavailable");
            using var gameStream = File.OpenRead(game!);
            using var structsStream = File.OpenRead(structs);
            var gameHash = Convert.ToHexString(SHA256.HashData(gameStream));
            var structsHash = Convert.ToHexString(SHA256.HashData(structsStream));
            return NativeBuildPolicy.Matches(gameHash, structsHash)
                ? new(true, "2026.10.06 native binaries match offline-reviewed layouts; live qualification pending")
                : new(false, $"Native build unsupported. Game={gameHash}; structures={structsHash}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return new(false, "Native binary fingerprint unavailable: " + error.GetType().Name); }
    }
}
