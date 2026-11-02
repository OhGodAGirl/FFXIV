using BetterGlamorPlates.Bootstrap;

namespace BetterGlamorPlates.Tests.Bootstrap;

/// <summary>Verifies runtime availability and bounded support observation decisions.</summary>
public sealed class RuntimeUiPolicyTests
{
    /// <summary>Both login and loaded player state are required before a character can own work.</summary>
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 42)]
    public void CharacterRequiresBothRuntimeFacts(bool loggedIn, bool loaded, ulong expected)
    {
        var reads = 0;
        var loadedReads = 0;
        Assert.Equal(expected, RuntimeUiPolicy.CharacterId(loggedIn, () => { loadedReads++; return loaded; }, () => { reads++; return 42; }));
        Assert.Equal(expected == 0 ? 0 : 1, reads);
        Assert.Equal(loggedIn ? 1 : 0, loadedReads);
    }

    /// <summary>Only the complete case-insensitive trimmed diagnostics subcommand opens support UI.</summary>
    [Theory]
    [InlineData("diagnostics", true)]
    [InlineData("  DIAGNOSTICS\t", true)]
    [InlineData("", false)]
    [InlineData("diagnostics extra", false)]
    [InlineData("diagnostic", false)]
    public void CommandParsingDoesNotAcceptPrefixesOrAdditionalArguments(string value, bool expected)
        => Assert.Equal(expected, RuntimeUiPolicy.IsDiagnosticsCommand(value));

    /// <summary>Examine status resets only when the viewed entity changes or is withdrawn.</summary>
    [Theory]
    [InlineData(null, null, false)]
    [InlineData(null, 1u, true)]
    [InlineData(1u, null, true)]
    [InlineData(1u, 1u, false)]
    [InlineData(1u, 2u, true)]
    public void ExamineIdentityTransitionControlsStatusReset(uint? previous, uint? current, bool expected)
        => Assert.Equal(expected, RuntimeUiPolicy.ExamineChanged(previous, current));

    /// <summary>A closed native editor cannot show or refresh the companion.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShowRequiresNativeEditorAndRefreshesAfterOpening(bool open)
    {
        var effects = new List<string>();
        RuntimeUiPolicy.ShowWhenAvailable(open, () => effects.Add("show"), () => effects.Add("refresh"));
        Assert.Equal(open ? ["show", "refresh"] : [], effects);
    }

    /// <summary>Support observations occur at exact half-second boundaries rather than every frame.</summary>
    [Fact]
    public void DiagnosticsCaptureIsBounded()
    {
        var schedule = new DiagnosticRefreshSchedule();
        var start = DateTimeOffset.UnixEpoch;
        Assert.True(schedule.Take(start));
        Assert.False(schedule.Take(start.AddMilliseconds(499)));
        Assert.True(schedule.Take(start.AddMilliseconds(500)));
        Assert.False(schedule.Take(start.AddMilliseconds(999)));
        Assert.True(schedule.Take(start.AddMilliseconds(1000)));
    }
}
