using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Features.CollectionData.Capture;

public sealed class SourceReadException(ReasonCode code) : Exception
{
    public ReasonCode Code { get; } = code;
}
