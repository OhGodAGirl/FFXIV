using Exportaramus.ConsumerKit.V1_2;

namespace Exportaramus.ConsumerKit.Tests;

internal sealed class FakeTransport : ICollectionDataTransport
{
    public string Capabilities { get; set; } = ContractFixture.Read("capabilities-valid.json");
    public string Snapshot { get; set; } = ContractFixture.Read("compact-valid-beasts-populated.json");
    public bool FailReads
    {
        get; set;
    }
    public bool FailReports
    {
        get; set;
    }
    public bool FailDisposal
    {
        get; set;
    }
    public string ReportResult { get; set; } = "Accepted";
    public Action? BeforeSnapshotReturns
    {
        get; set;
    }
    public int ReadCount
    {
        get; private set;
    }
    public int SnapshotReadCount
    {
        get; private set;
    }
    public int DisposeCount
    {
        get; private set;
    }
    public List<(string Character, uint Item, string Reporter)> Reports { get; } = [];

    public string ReadCapabilities()
    {
        ReadCount++;
        if (FailReads) throw new InvalidOperationException("Provider is absent.");
        return Capabilities;
    }

    public string ReadSnapshot()
    {
        SnapshotReadCount++;
        var payload = Snapshot;
        BeforeSnapshotReturns?.Invoke();
        return payload;
    }

    public string Report(string contentId, uint itemId, string reporter)
    {
        Reports.Add((contentId, itemId, reporter));
        if (FailReports) throw new InvalidOperationException("Provider stopped.");
        return ReportResult;
    }

    public void Dispose()
    {
        DisposeCount++;
        if (FailDisposal) throw new InvalidOperationException("Subscription cleanup failed.");
    }
}
