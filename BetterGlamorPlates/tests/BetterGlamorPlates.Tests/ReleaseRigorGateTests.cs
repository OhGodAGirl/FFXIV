using System.Diagnostics;
using System.Text.Json.Nodes;

namespace BetterGlamorPlates.Tests;

/// <summary>Proves the review-candidate waiver cannot excuse non-icon failures or alter historical evidence.</summary>
public sealed class ReleaseRigorGateTests
{
    /// <summary>Runs the actual gate against synthetic inventories, preserving the supplied policy bytes.</summary>
    [Theory]
    [InlineData("valid", true)]
    [InlineData("missing-receipt", false)]
    [InlineData("non-icon-failure", false)]
    [InlineData("wrong-count", false)]
    [InlineData("missing-icon", false)]
    [InlineData("duplicate-icon", false)]
    public async Task OnlyTheNamedIconRequirementCanBeDeferred(string scenario, bool accepted)
    {
        // Arrange.
        var root = FindRoot();
        var policy = JsonNode.Parse("""
            {
              "schemaVersion":1,"status":"complete",
              "propertyFuzz":{"applicable":false,"notApplicableReason":"Synthetic policy fixture."},
              "regression":{"command":"fixture","defectInventory":[
                {"id":"REG-ICON-ALPHA-PADDING","requirement":"Artwork","test":"Icon"},
                {"id":"REG-REAL-BEHAVIOR","requirement":"Behavior","test":"Behavior"}],
                "lastRun":{"declared":2,"passed":1,"failed":1,"skipped":0,"command":"historical"},
                "releaseCandidateLastRun":{"declared":1,"passed":1,"failed":0,"skipped":0,"command":"fixture"}},
              "lifecycle":{"command":"fixture","scenarioInventory":[
                {"id":"LIFE","requirement":"Dispose","test":"Dispose"}],
                "lastRun":{"declared":1,"passed":1,"failed":0,"skipped":0,"command":"fixture","stressIterations":1}}
            }
            """)!;
        var regression = policy["regression"]!;
        if (scenario == "missing-receipt") regression.AsObject().Remove("releaseCandidateLastRun");
        if (scenario == "non-icon-failure") regression["releaseCandidateLastRun"]!["failed"] = 1;
        if (scenario == "wrong-count") regression["releaseCandidateLastRun"]!["declared"] = 2;
        if (scenario == "missing-icon") regression["defectInventory"]!.AsArray().RemoveAt(0);
        if (scenario == "duplicate-icon") regression["defectInventory"]!.AsArray().Add(regression["defectInventory"]![0]!.DeepClone());
        var directory = Path.Combine(root, "tests", "TestResults", "release-rigor-fixtures");
        Directory.CreateDirectory(directory);
        var fixture = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
        var original = policy.ToJsonString();
        await File.WriteAllTextAsync(fixture, original);
        try
        {
            var start = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var argument in new[] { "-NoProfile", "-File", Path.Combine(root, "tests", "test-release-rigor.ps1"), "-PolicyPath", fixture })
                start.ArgumentList.Add(argument);

            // Act.
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            var output = await stdout + await stderr;

            // Assert.
            Assert.True((process.ExitCode == 0) == accepted, output);
            Assert.Equal(original, await File.ReadAllTextAsync(fixture));
            if (accepted) Assert.Contains("REG-ICON-ALPHA-PADDING is deferred", output, StringComparison.Ordinal);
        }
        finally { File.Delete(fixture); }
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "BetterGlamorPlates.csproj"))) return directory.FullName;
        throw new DirectoryNotFoundException("Plugin root unavailable.");
    }
}
