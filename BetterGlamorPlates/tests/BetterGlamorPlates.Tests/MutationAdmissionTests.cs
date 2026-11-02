using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace BetterGlamorPlates.Tests;

/// <summary>Proves equivalence review cannot silently admit untested or changed mutations.</summary>
public sealed class MutationAdmissionTests
{
    /// <summary>Exercises admission and every equivalence integrity boundary with synthetic reports.</summary>
    [Theory]
    [InlineData("killed", true)]
    [InlineData("equivalent", true)]
    [InlineData("survivor", false)]
    [InlineData("timeout", false)]
    [InlineData("uncovered", false)]
    [InlineData("stale-source", false)]
    [InlineData("stale-report", false)]
    [InlineData("changed-replacement", false)]
    [InlineData("missing-reviewer", false)]
    [InlineData("duplicate", false)]
    [InlineData("empty", false)]
    [InlineData("unfinished", false)]
    public async Task AdmissionIsBoundToReviewedTestedSource(string scenario, bool accepted)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "BetterGlamorPlates.csproj")))
            root = root.Parent ?? throw new DirectoryNotFoundException();
        var fixtures = Path.GetFullPath(Path.Combine(root.FullName, "tests", "TestResults", "mutation-admission"));
        var fixture = Path.GetFullPath(Path.Combine(fixtures, Guid.NewGuid().ToString("N")));
        Assert.StartsWith(fixtures + Path.DirectorySeparatorChar, fixture, StringComparison.OrdinalIgnoreCase);
        Directory.CreateDirectory(fixture);
        try
        {
            const string sourceText = "return value >> 1;\n";
            var source = Path.Combine(fixture, "Policy.cs");
            await File.WriteAllTextAsync(source, sourceText, new UTF8Encoding(false));
            var mutant = new JsonObject
            {
                ["id"] = "1",
                ["status"] = scenario switch
                {
                    "killed" => "Killed",
                    "timeout" => "Timeout",
                    "uncovered" => "NoCoverage",
                    "unfinished" => "NotRun",
                    _ => "Survived",
                },
                ["mutatorName"] = "Shift",
                ["replacement"] = "value >>> 1",
                ["location"] = new JsonObject
                {
                    ["start"] = new JsonObject { ["line"] = 1, ["column"] = 8 },
                    ["end"] = new JsonObject { ["line"] = 1, ["column"] = 18 },
                },
            };
            var killed = mutant.DeepClone().AsObject();
            killed["id"] = "0";
            killed["status"] = "Killed";
            killed["replacement"] = "false";
            var mutants = scenario == "empty" ? new JsonArray() : new JsonArray(killed, mutant);
            var report = new JsonObject
            {
                ["files"] = new JsonObject
                {
                    ["Policy.cs"] = new JsonObject
                    {
                        ["source"] = scenario == "stale-report" ? "changed" : sourceText,
                        ["mutants"] = mutants,
                    },
                },
            };
            var entry = new JsonObject
            {
                ["path"] = "Policy.cs",
                ["sourceSha256"] = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(source))),
                ["mutator"] = "Shift",
                ["replacement"] = scenario == "changed-replacement" ? "wrong" : "value >>> 1",
                ["startLine"] = 1,
                ["startColumn"] = 8,
                ["endLine"] = 1,
                ["endColumn"] = 18,
                ["rationale"] = "Synthetic admission fixture; no production exclusion.",
                ["reviewer"] = scenario == "missing-reviewer" ? "" : "Independent fixture reviewer",
            };
            var entries = scenario is "survivor" or "killed" or "empty" or "unfinished" ? new JsonArray() : new JsonArray(entry);
            if (scenario == "duplicate") entries.Add(entry.DeepClone());
            if (scenario == "stale-source") await File.AppendAllTextAsync(source, "changed");
            await File.WriteAllTextAsync(Path.Combine(fixture, "report.json"), report.ToJsonString());
            await File.WriteAllTextAsync(Path.Combine(fixture, "review.json"), new JsonObject
            {
                ["schemaVersion"] = 1,
                ["entries"] = entries,
            }.ToJsonString());
            var start = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var argument in new[] { "-NoProfile", "-File", Path.Combine(root.FullName, "tests", "test-mutation-report.ps1"),
                         "-ReportPath", Path.Combine(fixture, "report.json"), "-EquivalencePath", Path.Combine(fixture, "review.json"),
                         "-SourceRoot", fixture }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            var output = await stdout + await stderr;
            Assert.True(accepted == (process.ExitCode == 0), output);
            if (accepted) Assert.Contains("Valid mutation gate passed", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixture, recursive: true);
        }
    }
}
