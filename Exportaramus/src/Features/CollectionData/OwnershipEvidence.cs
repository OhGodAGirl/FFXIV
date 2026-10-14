using System.Collections.Immutable;
namespace Exportaramus.Features.CollectionData;

public sealed record OwnershipEvidence(string RuleCode, ImmutableArray<OwnershipFact> Facts);
