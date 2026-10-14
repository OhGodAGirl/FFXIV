using Exportaramus.Features.CollectionData.State;
namespace Exportaramus.Features.CollectionData;

public sealed record OwnershipFact(DataType DataType, string SourceKey, uint SubjectId, string State, SourceStatus SourceStatus);
