using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticCollect
{
    public string UpstreamCommit { get; init; } = "7eda07857e89b7dba81453135b44bc33bf3457b1";
    public DateTime VerifiedAtUtc { get; init; } = new(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc);
    public string ContractSha256 { get; init; } = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    public string MappingSha256 { get; init; } = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    public int IncludedKeyCount { get; init; }
    public int OmittedKeyCount { get; init; } = 16;
}
