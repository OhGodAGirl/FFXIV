using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Shared.CollectionData;
using BetterGlamorPlates.Shared.Operations;

namespace BetterGlamorPlates.Bootstrap;

/// <summary>Owns character-scoped collection refresh, stale-result rejection and publication.</summary>
internal sealed class CollectionCoordinator(
    OwnedOperationRegistry operations,
    Func<ulong> currentCharacter,
    Func<bool> takeRefresh,
    Func<ulong, CancellationToken, Task<CollectionDataParseResult>> refreshData,
    Func<Action, CancellationToken, Task> schedule,
    Func<CollectionDataSnapshot?> latest,
    Action clearCharacter,
    Action clearData,
    Action<CollectionDataParseResult> updateData,
    Action cancelPreview,
    Action<string> log)
{
    private ulong? observedContentId;
    private int sharedCollectionRefreshScheduled;
    private string? lastSharedCollectionDiagnostic;
    private CollectionDataSnapshot? displayedCollection;

    internal void ObserveSnapshot()
    {
        var current = latest();
        if (ReferenceEquals(displayedCollection, current)) return;
        displayedCollection = current;
        updateData(current is null
            ? CollectionDataParseResult.Reject(CollectionDataFailure.ProviderUnavailable)
            : CollectionDataParseResult.Accept(current));
    }

    internal void SynchronizeCharacter()
    {
        var currentContentId = currentCharacter();
        if (observedContentId == currentContentId)
            return;

        var hadCharacter = observedContentId is > 0;
        cancelPreview();
        observedContentId = currentContentId;
        clearCharacter();
        clearData();
        if (!hadCharacter && currentContentId > 0)
            return;

        LogSharedCollectionDiagnostic(
            currentContentId == 0
                ? "Shared collection data cleared because no character is active."
                : "Shared collection data cleared for a character change.");
    }

    internal void TryQueueRefresh()
    {
        var currentContentId = observedContentId.GetValueOrDefault();
        if (currentContentId == 0
            || Volatile.Read(ref sharedCollectionRefreshScheduled) != 0
            || !takeRefresh()
            || Interlocked.CompareExchange(ref sharedCollectionRefreshScheduled, 1, 0) != 0)
        {
            return;
        }

        if (!operations.TryStart(token => RefreshSharedCollectionDataAsync(currentContentId, token)))
        {
            Interlocked.Exchange(ref sharedCollectionRefreshScheduled, 0);
        }
    }

    private async Task RefreshSharedCollectionDataAsync(ulong requestedContentId, CancellationToken token)
    {
        try
        {
            var refresh = await refreshData(requestedContentId, token);

            await schedule(
                () =>
                {
                    var currentContentId = currentCharacter();
                    if (!CollectionProviderPolicy.CanPublishBackgroundRefresh(
                            requestedContentId,
                            currentContentId,
                            refresh.Failure,
                            token.IsCancellationRequested))
                    {
                        return;
                    }

                    PublishSharedCollectionData(refresh);
                },
                token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            Interlocked.Exchange(ref sharedCollectionRefreshScheduled, 0);
        }
    }

    private void PublishSharedCollectionData(CollectionDataParseResult refresh)
    {
        switch (refresh.Failure)
        {
            case CollectionDataFailure.None:
                updateData(refresh);
                LogSharedCollectionDiagnostic(DescribeAcceptedSharedCollection(refresh.Snapshot!));
                return;
            case CollectionDataFailure.NoCharacter:
                clearData();
                LogSharedCollectionDiagnostic(
                    "Shared collection data skipped because no character is active.");
                return;
            case CollectionDataFailure.WrongCharacter:
                clearCharacter();
                clearData();
                LogSharedCollectionDiagnostic(
                    "Shared collection data was rejected for another character and cleared.");
                return;
            default:
                var retainedSnapshot = latest();
                updateData(refresh);
                LogSharedCollectionDiagnostic(
                    retainedSnapshot is null
                        ? $"Shared collection data unavailable: {refresh.Failure}."
                        : $"Shared collection data refresh failed: {refresh.Failure}. Retaining the last accepted {retainedSnapshot.ProviderInternalName} {retainedSnapshot.ProviderVersion} ownership snapshot with current and last-known source labels.");
                return;
        }
    }

    private void LogSharedCollectionDiagnostic(string message)
    {
        if (string.Equals(lastSharedCollectionDiagnostic, message, StringComparison.Ordinal))
            return;

        lastSharedCollectionDiagnostic = message;
        log(message);
    }

    private static string DescribeAcceptedSharedCollection(CollectionDataSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return
            $"Shared collection data accepted from {snapshot.ProviderInternalName} {snapshot.ProviderVersion}: inventory {DescribeSource(snapshot.Inventory)}, armory chest {DescribeSource(snapshot.ArmoryChest)}, chocobo saddlebag {DescribeSource(snapshot.ChocoboSaddlebag)}, premium saddlebag {DescribeSource(snapshot.PremiumSaddlebag)}, retainers {DescribeSource(snapshot.Retainers)}, armoire {DescribeSource(snapshot.Armoire)}, glamour dresser {DescribeSource(snapshot.GlamourDresser)}.";
    }

    private static string DescribeSource(CollectionSourceSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return $"{source.Status} ({source.ItemIds.Length:N0} item IDs)";
    }
}
