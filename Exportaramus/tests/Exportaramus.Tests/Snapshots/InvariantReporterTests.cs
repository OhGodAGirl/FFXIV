using System.Diagnostics;
using System.Security.Cryptography;
using Exportaramus.Features.CollectionData.Publication;

namespace Exportaramus.Tests.Snapshots;

public sealed class InvariantReporterTests
{
    [Fact]
    public async Task InvalidUnicodeReporterIsRejectedInInvariantMode()
    {
        const string marker = "EXPORTARAMUS_REPORTER_INVARIANT_PROBE";
        if (Environment.GetEnvironmentVariable(marker) == "1")
        {
            Assert.Equal(Environment.GetEnvironmentVariable("EXPORTARAMUS_REPORTER_PROBE_HASH"),
                Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(typeof(ReportOwnershipEvidencePolicy).Assembly.Location))));
            Assert.True(ReportOwnershipEvidencePolicy.TryNormalize("123", 1, "Reporter", out _, out var valid));
            Assert.Equal("Reporter", valid);
            Assert.False(ReportOwnershipEvidencePolicy.TryNormalize("123", 1, "\ud800", out _, out _));
            await File.WriteAllTextAsync(Environment.GetEnvironmentVariable("EXPORTARAMUS_REPORTER_PROBE_RECEIPT")!, "Completed");
            return;
        }
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT")!, "dotnet.exe");
        var start = new ProcessStartInfo(dotnet) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(typeof(InvariantReporterTests).Assembly.Location);
        start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=Exportaramus.Tests.Snapshots.InvariantReporterTests.InvalidUnicodeReporterIsRejectedInInvariantMode");
        start.Environment[marker] = "1";
        start.Environment["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1";
        start.Environment["EXPORTARAMUS_REPORTER_PROBE_HASH"] = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(typeof(ReportOwnershipEvidencePolicy).Assembly.Location)));
        var receipt = Path.Combine(AppContext.BaseDirectory, "reporter-probe-" + Guid.NewGuid().ToString("N") + ".txt");
        start.Environment["EXPORTARAMUS_REPORTER_PROBE_RECEIPT"] = receipt;
        using var child = Process.Start(start)!;
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync();
        Assert.True(child.ExitCode == 0, await output + await error);
        Assert.Equal("Completed", await File.ReadAllTextAsync(receipt));
        File.Delete(receipt);
    }
}
