namespace Exportaramus.Features.CollectionData.State;

public sealed record QuestState(uint Id, QuestStatus Status, byte? Sequence);
