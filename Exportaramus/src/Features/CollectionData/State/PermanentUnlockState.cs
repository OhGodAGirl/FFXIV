using System.Collections.Immutable;
namespace Exportaramus.Features.CollectionData.State;

public sealed record PermanentUnlockState(ImmutableArray<uint> UnlockedIds, CollectionSourceStatus Health);
