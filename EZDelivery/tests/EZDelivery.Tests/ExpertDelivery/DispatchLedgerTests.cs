using EZDelivery.Features.ExpertDelivery;

namespace EZDelivery.Tests.ExpertDelivery;

public sealed class DispatchLedgerTests
{
    [Fact]
    public void InitialLedgerCannotRefreshOrAcceptNonpositiveSelection()
    {
        // Arrange
        var ledger = new DispatchLedger();

        // Act
        var zero = ledger.TryBeginSelection(0);
        var negative = ledger.TryBeginSelection(-1);
        var refresh = ledger.TryRecordRefresh(0);

        // Assert
        Assert.False(zero);
        Assert.False(negative);
        Assert.False(refresh);
        Assert.Equal(0, ledger.SelectionGeneration);
        Assert.False(ledger.IsDuplicate(1, DeliveryCommandKind.Select, 0));
        Assert.False(ledger.IsDuplicate(0, DeliveryCommandKind.Select, 0));
        Assert.False(ledger.CanRefresh(1));
    }

    [Fact]
    public void DuplicateReservationIncludesCommandKindAndDialogIdentity()
    {
        // Arrange
        var ledger = new DispatchLedger();
        Assert.True(ledger.TryBeginSelection(7));

        // Act
        var first = ledger.TryRecordDispatch(7, DeliveryCommandKind.ConfirmHq, 10);
        var duplicate = ledger.TryRecordDispatch(7, DeliveryCommandKind.ConfirmHq, 10);
        var otherDialog = ledger.TryRecordDispatch(7, DeliveryCommandKind.ConfirmHq, 11);
        var otherKind = ledger.TryRecordDispatch(7, DeliveryCommandKind.Submit, 10);

        // Assert
        Assert.True(first);
        Assert.False(duplicate);
        Assert.True(otherDialog);
        Assert.True(otherKind);
        Assert.True(ledger.IsDuplicate(7, DeliveryCommandKind.ConfirmHq, 10));
        Assert.False(ledger.IsDuplicate(8, DeliveryCommandKind.ConfirmHq, 10));
        Assert.False(ledger.IsDuplicate(7, DeliveryCommandKind.CancelReward, 10));
        Assert.False(ledger.IsDuplicate(7, DeliveryCommandKind.ConfirmHq, 12));
    }

    [Fact]
    public void RejectedSelectionLeavesExistingDispatchAndRefreshReservationsIntact()
    {
        // Arrange
        var ledger = new DispatchLedger();
        Assert.True(ledger.TryBeginSelection(7));
        Assert.True(ledger.TryRecordDispatch(7, DeliveryCommandKind.Submit, 10));
        Assert.True(ledger.TryRecordRefresh(7));

        // Act
        var same = ledger.TryBeginSelection(7);
        var older = ledger.TryBeginSelection(6);

        // Assert
        Assert.False(same);
        Assert.False(older);
        Assert.Equal(7, ledger.SelectionGeneration);
        Assert.True(ledger.IsDuplicate(7, DeliveryCommandKind.Submit, 10));
        Assert.False(ledger.CanRefresh(7));
        Assert.False(ledger.TryRecordRefresh(7));
    }

    [Fact]
    public void NewSelectionReleasesPreviousTokensButRejectsOldGenerationsAndRefreshes()
    {
        // Arrange
        var ledger = new DispatchLedger();
        ledger.TryBeginSelection(7);
        ledger.TryRecordDispatch(7, DeliveryCommandKind.Submit, 10);
        ledger.TryRecordRefresh(7);

        // Act
        var begun = ledger.TryBeginSelection(8);

        // Assert
        Assert.True(begun);
        Assert.Equal(8, ledger.SelectionGeneration);
        Assert.False(ledger.IsDuplicate(7, DeliveryCommandKind.Submit, 10));
        Assert.False(ledger.IsNewSelection(7));
        Assert.False(ledger.IsNewSelection(8));
        Assert.True(ledger.IsNewSelection(9));
        Assert.False(ledger.TryRecordRefresh(7));
        Assert.False(ledger.TryRecordRefresh(9));
        Assert.True(ledger.CanRefresh(8));
        Assert.True(ledger.TryRecordDispatch(8, DeliveryCommandKind.Submit, 10));
        Assert.True(ledger.TryRecordRefresh(8));
    }

    [Fact]
    public void PossiblySentCallbackCannotBeRetriedAfterItsException()
    {
        // Arrange
        var ledger = new DispatchLedger();
        ledger.TryBeginSelection(1);
        var calls = 0;
        void Attempt()
        {
            if (!ledger.TryRecordDispatch(1, DeliveryCommandKind.Submit, 99)) return;
            calls++;
            throw new InvalidOperationException("Native callback may already have sent the request.");
        }

        // Act
        Assert.Throws<InvalidOperationException>(Attempt);
        Attempt();

        // Assert
        Assert.Equal(1, calls);
        Assert.True(ledger.IsDuplicate(1, DeliveryCommandKind.Submit, 99));
    }

    [Fact]
    public void PossiblySentRefreshCannotBeRetriedAfterItsException()
    {
        // Arrange
        var ledger = new DispatchLedger();
        ledger.TryBeginSelection(1);
        var calls = 0;
        void Attempt()
        {
            if (!ledger.TryRecordRefresh(1)) return;
            calls++;
            throw new InvalidOperationException("Native refresh may already have been accepted.");
        }

        // Act
        Assert.Throws<InvalidOperationException>(Attempt);
        Attempt();

        // Assert
        Assert.Equal(1, calls);
        Assert.False(ledger.CanRefresh(1));
    }

    [Fact]
    public void RepeatedSelectionLifetimesResetOnlyThePreviousOperationTokens()
    {
        // Arrange
        var ledger = new DispatchLedger();

        for (var generation = 1; generation <= 100; generation++)
        {
            // Act
            var started = ledger.TryBeginSelection(generation);

            // Assert
            Assert.True(started);
            Assert.False(ledger.IsDuplicate(generation - 1, DeliveryCommandKind.Select, 0));
            Assert.True(ledger.TryRecordDispatch(generation, DeliveryCommandKind.Select, 0));
            Assert.False(ledger.TryRecordDispatch(generation, DeliveryCommandKind.Select, 0));
            Assert.True(ledger.TryRecordRefresh(generation));
            Assert.False(ledger.TryRecordRefresh(generation));
        }
    }
}
