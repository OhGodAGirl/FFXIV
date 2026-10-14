using Exportaramus.Features.CollectionData.State;
namespace Exportaramus.Features.CollectionData;

public sealed record CollectionItem(uint Id, string Name, bool Owned, OwnershipEvidence OwnershipEvidence,
    QuestStatus? Status = null, byte? Sequence = null, bool? RewardCollected = null);
