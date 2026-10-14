using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Tests.Capture;

public sealed class BeastReadPolicyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(50)]
    public void CompleteReceivedListIncludesExactlyObservedUnlocks(int count)
    {
        var calls = new List<uint>();
        var result = BeastReadPolicy.Read(true, count, id => { calls.Add(id); return id <= count; }, () => true);
        Assert.Equal(Enumerable.Range(1, 50).Select(x => (uint)x), calls);
        Assert.Equal(Enumerable.Range(1, count).Select(x => (uint)x), result.Order());
    }

    [Fact]
    public void PendingListNeverLooksLikeNoUnlocks() => Assert.Equal(ReasonCode.NotLoadedThisSession,
        Assert.Throws<SourceReadException>(() => BeastReadPolicy.Read(false, 0,
            _ => throw new InvalidOperationException(), () => true)).Code);

    [Theory]
    [InlineData(-1)]
    [InlineData(51)]
    public void InvalidNativeCountIsRejectedBeforeQueries(int count) => Assert.Equal(ReasonCode.CaptureFailed,
        Assert.Throws<SourceReadException>(() => BeastReadPolicy.Read(true, count,
            _ => throw new InvalidOperationException(), () => true)).Code);

    [Fact]
    public void InconsistentCountCannotPublishPartialOwnership() => Assert.Equal(ReasonCode.CaptureFailed,
        Assert.Throws<SourceReadException>(() => BeastReadPolicy.Read(true, 2, id => id == 1, () => true)).Code);

    [Fact]
    public void ChangedNativeStateCannotPublishOwnership() => Assert.Equal(ReasonCode.CaptureFailed,
        Assert.Throws<SourceReadException>(() => BeastReadPolicy.Read(true, 1, id => id == 1, () => false)).Code);

    [Fact]
    public void ExactDirectoryIsAccepted() => BeastReadPolicy.ValidateCatalog(51, true, id => id is >= 1 and <= 50);

    [Theory]
    [InlineData(50, true, 0)]
    [InlineData(52, true, 0)]
    [InlineData(51, false, 0)]
    [InlineData(51, true, 50)]
    public void ChangedOrIncompleteDirectoryFailsClosed(int count, bool zero, uint missing) =>
        Assert.Equal(ReasonCode.MappingValidationFailed,
            Assert.Throws<SourceReadException>(() => BeastReadPolicy.ValidateCatalog(count, zero, id => id != missing)).Code);
}
