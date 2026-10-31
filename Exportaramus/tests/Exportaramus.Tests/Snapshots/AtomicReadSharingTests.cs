using System.Text;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class AtomicReadSharingTests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "isolated-test-recovery/read-sharing", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeldAcceptedReadAllowsAnotherReaderAndAtomicReplacementOrDeletion(bool delete)
    {
        Directory.CreateDirectory(root);
        var destination = Path.Combine(root, "current-snapshot.json");
        var candidate = Path.Combine(root, "candidate.tmp");
        const string accepted = "{\"accepted\":true}";
        const string replacement = "{\"replacement\":true}";
        await File.WriteAllTextAsync(destination, accepted);
        await File.WriteAllTextAsync(candidate, replacement);
        var operations = new AtomicFileOperations();
        await using var held = operations.OpenRead(destination);
        await using var second = operations.OpenRead(destination);

        if (delete) operations.Delete(destination);
        else operations.Replace(candidate, destination, null);

        using var heldReader = new StreamReader(held, Encoding.UTF8, leaveOpen: true);
        using var secondReader = new StreamReader(second, Encoding.UTF8, leaveOpen: true);
        Assert.Equal(accepted, await heldReader.ReadToEndAsync());
        Assert.Equal(accepted, await secondReader.ReadToEndAsync());
        if (delete) Assert.False(File.Exists(destination));
        else Assert.Equal(replacement, await File.ReadAllTextAsync(destination));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
