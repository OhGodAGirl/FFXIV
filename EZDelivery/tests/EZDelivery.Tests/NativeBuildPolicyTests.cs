using EZDelivery.Bootstrap;
using Xunit;

namespace EZDelivery.Tests;

public sealed class NativeBuildPolicyTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("ffxiv.exe", false)]
    [InlineData("ffxiv_dx11.exe.bak", false)]
    [InlineData("C:/game/ffxiv_dx11.exe", true)]
    [InlineData("C:/game/FFXIV_DX11.EXE", true)]
    public void ExecutableIdentityIsExact(string? path, bool expected)
        => Assert.Equal(expected, NativeBuildPolicy.IsGameProcess(path));

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    public void BothReviewedFingerprintsAreRequired(bool game, bool structs, bool expected)
        => Assert.Equal(expected, NativeBuildPolicy.Matches(game ? NativeBuildPolicy.GameSha256 : "wrong", structs ? NativeBuildPolicy.StructsSha256 : "wrong"));
}
