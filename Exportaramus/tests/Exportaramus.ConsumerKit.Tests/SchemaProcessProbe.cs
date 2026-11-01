using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Exportaramus.ConsumerKit.V1_2;

namespace Exportaramus.ConsumerKit.Tests;

internal static class SchemaProcessProbe
{
    // A fatal recursive-validation error must fail its test without aborting the whole suite.
    public static int Main(string[] args)
    {
        if (args.Length != 3 || args[0] != "--schema-validation-probe") return 2;
        Console.WriteLine(ConsumerAssemblyHash());
        var bytes = Encoding.UTF8.GetBytes("{\"$id\":\"https://example.test/collection-data.schema.json\"," + args[1] + "}");
        var registry = new ContractSchemaRegistry([bytes]);
        using var document = JsonDocument.Parse(args[2]);
        WithValidationStack(() => Console.WriteLine(registry.Validate(document.RootElement, "collection-data.schema.json")));
        return 0;
    }

    internal static void WithValidationStack(Action validation)
    {
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try { validation(); }
            catch (Exception exception) { failure = exception; }
        }, 16 * 1024 * 1024);
        worker.Start();
        worker.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    internal static T WithValidationStack<T>(Func<T> validation)
    {
        T result = default!;
        WithValidationStack(() => { result = validation(); });
        return result;
    }

    internal static void AssertValidation(string schema, string payload, bool accepted)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        start.ArgumentList.Add(typeof(SchemaProcessProbe).Assembly.Location);
        start.ArgumentList.Add("--schema-validation-probe");
        start.ArgumentList.Add(schema);
        start.ArgumentList.Add(payload);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var lines = output.GetAwaiter().GetResult().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var error = errors.GetAwaiter().GetResult();
        Assert.NotEmpty(lines);
        Assert.Equal(ConsumerAssemblyHash(), lines[0]);
        Assert.True(process.ExitCode == 0,
            $"Schema probe {start.FileName} {string.Join(' ', start.ArgumentList)} loaded SHA256 {lines[0]} and exited {process.ExitCode}: {error[..Math.Min(error.Length, 2000)]}");
        Assert.Equal(2, lines.Length);
        Assert.Equal(accepted.ToString(), lines[1]);
    }

    private static string ConsumerAssemblyHash() =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(ContractSchemaRegistry).Assembly.Location)));
}
