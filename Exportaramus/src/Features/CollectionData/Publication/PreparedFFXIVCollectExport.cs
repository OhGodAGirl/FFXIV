using System.Collections.Immutable;
using System.Text;
using Exportaramus.Features.CollectionData.Contracts;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Shared.Serialization;
namespace Exportaramus.Features.CollectionData.Publication;

public sealed record PreparedFFXIVCollectExport(string? Json, ImmutableArray<string> IncludedKeys, ImmutableArray<string> OmittedKeys, string? Error)
{
    public bool CanCopy => Json is not null;
    public static PreparedFFXIVCollectExport Unavailable(string reason) => new(null, [], CollectionKeys.Collect, reason);
}

