using BetterGlamorPlates.Bootstrap;
using BetterGlamorPlates.Features.Diagnostics;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Shared.Operations;

namespace BetterGlamorPlates.Tests.Bootstrap;

/// <summary>Verifies queued Examine import, ownership, failures and cleanup.</summary>
public sealed class ExamineSaveCoordinatorTests
{
    /// <summary>Duplicate clicks capture once and preserve the requested examined identity.</summary>
    [Fact]
    public async Task DuplicateSaveCapturesAndImportsOnce()
    {
        using var fixture = new Fixture();
        fixture.Save.Save(7, "Player");
        fixture.Save.Save(8, "Other");
        Assert.Equal([true], fixture.Saving);
        await fixture.Run();
        Assert.Equal([(7u, "Player")], fixture.Captures);
        Assert.Same(fixture.Plate, Assert.Single(fixture.Imported));
        Assert.Equal([true, false], fixture.Saving);
        Assert.Equal("Saved Outfit.", fixture.Status);
        Assert.Equal(DiagnosticStop.Completed, fixture.Diagnostics.Current.Stop);
        Assert.Equal(1, fixture.Diagnostics.Current.ConfirmedActions);
        Assert.Equal(DiagnosticPhase.ExamineSave, fixture.Diagnostics.Current.Phase);
        Assert.Equal(DateTimeOffset.UnixEpoch, fixture.Diagnostics.Current.ObservedAtUtc);
        Assert.Collection(fixture.Diagnostics.Current.Checks,
            check => AssertCheck(check, DiagnosticKey.LocalOwner, true),
            check => AssertCheck(check, DiagnosticKey.OutfitCreated, true));
    }

    /// <summary>Missing and changed local characters never reach capture or persistence.</summary>
    [Theory]
    [InlineData(0, 42)]
    [InlineData(42, 99)]
    public async Task OwnerGuardPreventsSavingForAnotherCharacter(ulong owner, ulong current)
    {
        using var fixture = new Fixture { Owner = owner };
        fixture.Save.Save(7, "Player");
        fixture.Owner = current;
        await fixture.Run();
        Assert.Empty(fixture.Captures);
        Assert.Empty(fixture.Imported);
        Assert.Equal("Your character changed. Nothing was saved.", fixture.Status);
        Assert.Equal(DiagnosticStop.CharacterChanged, fixture.Diagnostics.Current.Stop);
        Assert.Equal([true, false], fixture.Saving);
        AssertCheck(Assert.Single(fixture.Diagnostics.Current.Checks), DiagnosticKey.LocalOwner, false);
    }

    /// <summary>A callback reached after shutdown performs no capture or write.</summary>
    [Fact]
    public async Task CancelledCallbackDoesNotCapture()
    {
        using var fixture = new Fixture();
        fixture.Save.Save(7, "Player");
        fixture.Operations.Dispose();
        await fixture.Run();
        Assert.Empty(fixture.Captures);
        Assert.Equal(DiagnosticStop.Cancelled, fixture.Diagnostics.Current.Stop);
        Assert.Equal(string.Empty, fixture.Status);
    }

    /// <summary>An Examine view that changed before capture is rejected without persistence.</summary>
    [Fact]
    public async Task MissingCaptureDoesNotImport()
    {
        using var fixture = new Fixture { Plate = null };
        fixture.Save.Save(7, "Player");
        await fixture.Run();
        Assert.Empty(fixture.Imported);
        Assert.Equal(DiagnosticStop.Rejected, fixture.Diagnostics.Current.Stop);
        Assert.Equal("Examine changed or is still loading. Nothing was saved.", fixture.Status);
        Assert.Collection(fixture.Diagnostics.Current.Checks,
            check => AssertCheck(check, DiagnosticKey.LocalOwner, true),
            check => AssertCheck(check, DiagnosticKey.OutfitCreated, false));
    }

    /// <summary>Capture and persistence failures are retained and never automatically retried.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedCaptureOrImportIsReportedWithoutRetry(bool failImport)
    {
        using var fixture = new Fixture { Failure = new IOException("test failure"), FailImport = failImport };
        fixture.Save.Save(7, "Player");
        await fixture.Run();
        Assert.Same(fixture.Failure, Assert.Single(fixture.Errors));
        Assert.Equal(DiagnosticStop.Exception, fixture.Diagnostics.Current.Stop);
        Assert.Equal(0, fixture.Diagnostics.Current.ConfirmedActions);
        Assert.Equal("The outfit could not be saved. Check diagnostics before retrying.", fixture.Status);
        Assert.Equal([true, false], fixture.Saving);
        Assert.Single(fixture.Captures);
    }

    /// <summary>Rejected operation starts clear the busy state so no pending UI state survives.</summary>
    [Fact]
    public void DisposedRegistryRejectsSaveAndClearsBusyState()
    {
        using var fixture = new Fixture();
        fixture.Operations.Dispose();
        fixture.Save.Save(7, "Player");
        fixture.Save.Save(7, "Player");
        Assert.Equal([true, false, true, false], fixture.Saving);
        Assert.Null(fixture.Callback);
        Assert.Empty(fixture.Captures);
    }

    private static void AssertCheck(DiagnosticCheck check, DiagnosticKey key, bool passed)
    {
        Assert.Equal(key, check.Key);
        Assert.Equal(1L, check.Expected);
        Assert.Equal(passed ? 1L : 0L, check.Observed);
        Assert.Equal(passed ? DiagnosticStatus.Pass : DiagnosticStatus.Failure, check.Status);
        Assert.Null(check.Slot);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly OwnedOperationRegistry Operations;
        internal readonly ExamineSaveCoordinator Save;
        internal readonly DiagnosticJournal Diagnostics = new();
        internal ulong Owner = 42;
        internal SavedPlate? Plate = new() { Id = Guid.NewGuid(), Name = "Outfit" };
        internal readonly List<(uint Entity, string Name)> Captures = [];
        internal readonly List<SavedPlate> Imported = [];
        internal readonly List<bool> Saving = [];
        internal readonly List<Exception> Errors = [];
        internal string Status = string.Empty;
        internal Exception? Failure;
        internal bool FailImport;
        internal Action? Callback;
        private readonly TaskCompletionSource completion = new();

        internal Fixture()
        {
            Operations = new(Errors.Add);
            Save = new(Operations, (action, _) => { Callback = action; return completion.Task; }, () => Owner,
                (entity, name) =>
                {
                    Captures.Add((entity, name));
                    if (Failure is not null && !FailImport) throw Failure;
                    return Plate;
                }, plate =>
                {
                    if (FailImport) throw Failure!;
                    Imported.Add(plate);
                }, Saving.Add, status => Status = status, Diagnostics, () => DateTimeOffset.UnixEpoch, Errors.Add);
        }

        internal async Task Run()
        {
            try { Callback!(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(exception); }
            await Operations.WhenIdle.WaitAsync(TimeSpan.FromSeconds(1));
        }

        public void Dispose() => Operations.Dispose();
    }
}

