using EZDelivery.Features.ExpertDelivery;
using Xunit;

namespace EZDelivery.Tests;

public sealed class DeliverySelectionMappingTests
{
    private static DeliverySelectionRecord[] Records(params DeliverySelectionRecord[] expert)
        => Enumerable.Repeat(new DeliverySelectionRecord(0, false, 1, 0, false), 11).Concat(expert).ToArray();

    [Fact]
    public void RemovedSourcesDoNotLeaveVisibleIndexHoles()
    {
        var records = Records(new(2, true, 1, 0, false), new(2, false, 0, 0, false), new(2, false, 1, 0, false));
        Assert.True(DeliverySelectionMapping.TryResolve(records, Enumerable.Range(0, records.Length).ToArray(), 0, 1, out var selected));
        Assert.Equal(new[] { 13 }, selected);
    }

    [Fact]
    public void NativeSortOrderIsPreservedAcrossDuplicateItems()
    {
        var records = Records(new(2, false, 1, 0, false), new(2, false, 1, 0, false));
        var order = Enumerable.Range(0, records.Length).ToArray();
        (order[11], order[12]) = (order[12], order[11]);
        Assert.True(DeliverySelectionMapping.TryResolve(records, order, 0, 2, out var selected));
        Assert.Equal(new[] { 12, 11 }, selected);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    public void NativeFilterControlsExactlyWhichSourcesSurvive(int filter, int count)
    {
        var records = Records(new(2, false, 1, 0, true), new(2, false, 1, 3200, false), new(2, false, 1, 3500, false));
        Assert.True(DeliverySelectionMapping.TryResolve(records, Enumerable.Range(0, records.Length).ToArray(), filter, count, out var selected));
        Assert.Equal(count, selected.Count);
    }

    [Fact]
    public void RemovedFinalRowMustClearFromPresentationBeforeReady()
    {
        var records = Records(new DeliverySelectionRecord(2, true, 1, 0, false));
        Assert.False(DeliverySelectionMapping.TryResolve(records, Enumerable.Range(0, records.Length).ToArray(), 0, 1, out var selected));
        Assert.Empty(selected);
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(8, true)]
    [InlineData(7, false)]
    public void NativeEightSurvivorsNeverProduceNinthSelection(int displayed, bool valid)
    {
        var records = Records(Enumerable.Repeat(new DeliverySelectionRecord(2, false, 1, 0, false), 8).ToArray());
        Assert.Equal(valid, DeliverySelectionMapping.TryResolve(records, Enumerable.Range(0, records.Length).ToArray(), 1, displayed, out var selected));
        Assert.Equal(valid ? 8 : 0, selected.Count);
    }

    [Fact]
    public void RepeatedExcludedIndicesReportCorruptPermutation()
    {
        var records = Records(new DeliverySelectionRecord(2, false, 1, 0, false));
        var order = Enumerable.Range(0, records.Length).ToArray();
        order[1] = 0;
        Assert.False(DeliverySelectionMapping.TryResolve(records, order, 0, 1, out _, out var check));
        Assert.Contains("Duplicate sorted index", check);
    }

    [Fact]
    public void RepeatedEligibleIndexIsNeverAnAdditionalDelivery()
    {
        var records = Records(new(2, false, 1, 0, false), new(2, false, 1, 0, false));
        var order = Enumerable.Range(0, records.Length).ToArray();
        order[12] = 11;
        Assert.False(DeliverySelectionMapping.TryResolve(records, order, 0, 2, out _, out var check));
        Assert.Contains("Duplicate sorted index", check);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(12)]
    [InlineData(0)]
    public void InvalidOrDuplicateSortedIndexRejectsEntireMap(int invalid)
    {
        var records = Records(new DeliverySelectionRecord(2, false, 1, 0, false));
        var order = Enumerable.Range(0, records.Length).ToArray();
        order[11] = invalid;
        Assert.False(DeliverySelectionMapping.TryResolve(records, order, 0, 1, out _));
    }
}
