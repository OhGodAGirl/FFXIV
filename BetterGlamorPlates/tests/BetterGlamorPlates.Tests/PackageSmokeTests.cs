namespace BetterGlamorPlates.Tests;

using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;

/// <summary>Exercises package admission against real payload bytes and deliberately broken fixtures.</summary>
public sealed class PackageSmokeTests
{
    /// <summary>Accepts a complete matching bundle and rejects broken or contaminated packages.</summary>
    [Theory]
    [InlineData("valid", null)]
    [InlineData("missing-consumer", "Package is missing BetterGlamorPlates.Exportaramus.dll")]
    [InlineData("missing-icon", "Package is missing images/icon.png")]
    [InlineData("private-file", "Unexpected package entry")]
    [InlineData("duplicate-entry", "Duplicate package entry")]
    [InlineData("changed-bytes", "Package bytes differ")]
    [InlineData("wrong-version", "assembly version mismatch")]
    [InlineData("bad-icon", "Plugin icon is not a PNG")]
    public async Task ChecksRealBundleAndBrokenPackages(string scenario, string? expectedFailure)
    {
        var root = FindRoot();
        var fixtures = Path.Combine(root, "tests", "TestResults", "smoke-fixtures");
        var fixture = Path.Combine(fixtures, Guid.NewGuid().ToString("N"));
        var resolved = Path.GetFullPath(fixture);
        if (!resolved.StartsWith(Path.GetFullPath(fixtures) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fixture cleanup escaped its test directory.");
        var bundle = Path.Combine(fixture, "bundle");
        Directory.CreateDirectory(Path.Combine(bundle, "images"));
        string[] names = ["BetterGlamorPlates.dll", "BetterGlamorPlates.Exportaramus.dll",
            "BetterGlamorPlates.json", "BetterGlamorPlates.deps.json", "images/icon.png"];
        try
        {
            foreach (var name in names)
                File.Copy(Path.Combine(root, "bin", "Release", name), Path.Combine(bundle, name));
            if (scenario == "wrong-version")
            {
                var path = Path.Combine(bundle, "BetterGlamorPlates.json");
                var manifest = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                manifest["AssemblyVersion"] = "0.0.0.0";
                File.WriteAllText(path, manifest.ToJsonString());
            }
            if (scenario == "bad-icon")
                File.WriteAllText(Path.Combine(bundle, "images/icon.png"), "invalid");
            var package = Path.Combine(fixture, "latest.zip");
            using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                foreach (var name in names)
                {
                    if (scenario == "missing-consumer" && name == "BetterGlamorPlates.Exportaramus.dll"
                        || scenario == "missing-icon" && name == "images/icon.png") continue;
                    zip.CreateEntryFromFile(Path.Combine(bundle, name), name);
                }
                if (scenario == "duplicate-entry")
                    zip.CreateEntryFromFile(Path.Combine(bundle, names[0]), names[0]);
                if (scenario == "private-file")
                {
                    using var writer = new StreamWriter(zip.CreateEntry("development/private.txt").Open());
                    writer.Write("Synthetic fixture only");
                }
            }
            if (scenario == "changed-bytes")
                File.AppendAllText(Path.Combine(bundle, "BetterGlamorPlates.deps.json"), " ");
            var start = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = root,
            };
            foreach (var argument in new[] { "-NoProfile", "-File", Path.Combine(root, "tests", "test-smoke.ps1"),
                         "-BundlePath", bundle, "-PackagePath", package }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            var result = await stdout + await stderr;
            if (expectedFailure is null)
            {
                Assert.True(process.ExitCode == 0, result);
                Assert.Contains("Package smoke passed", result, StringComparison.Ordinal);
            }
            else
            {
                Assert.NotEqual(0, process.ExitCode);
                Assert.Contains(expectedFailure, result, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(resolved, recursive: true);
        }
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "BetterGlamorPlates.csproj"))) return directory.FullName;
        throw new DirectoryNotFoundException("Plugin root unavailable.");
    }
}
