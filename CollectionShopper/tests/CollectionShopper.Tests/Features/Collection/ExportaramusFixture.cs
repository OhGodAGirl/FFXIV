using Exportaramus.ConsumerKit.V1_2;

namespace CollectionShopper.Tests;

internal static class ExportaramusFixture
{
    internal const ulong ContentId = 12345678901234567;
    internal static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    internal static string Read(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "tests", "fixtures", "Contracts", "1.2")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, "tests", "fixtures", "Contracts", "1.2", "fixtures", "valid", name));
    }

    internal static CollectionDataSnapshot Snapshot()
    {
        var result = CollectionDataSemanticValidator.ValidateSnapshot(Read("compact-valid-full.json"), ContentId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(result.Success, result.Detail);
        return result.Value!;
    }

    internal static CollectionDataOwnershipState State(CollectionDataSnapshot? snapshot = null)
    {
        snapshot ??= Snapshot();
        return CollectionDataOwnershipPolicy.Build(new(CollectionDataAvailability.Ready, snapshot,
            snapshot.ProviderSessionId, snapshot.ContentId, snapshot.Revision, CollectionDataValidationCode.None), ContentId);
    }
}
