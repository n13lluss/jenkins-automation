using PipelineForge.Core.Model;

namespace PipelineForge.Core.Ai;

public sealed record AiConnectionInfo(AiProvider Provider, string Model, Uri? BaseUrl, string ApiKey);

public sealed record AiSuggestion(string Severity, string Title, string File, string Detail);

public sealed record AiReviewResult(string Summary, IReadOnlyList<AiSuggestion> Suggestions, string? Problem, string PayloadSha256);

/// <summary>Reviews generated pipeline files. Callers pass already-redacted content.</summary>
public interface IAiReviewProvider
{
    AiProvider Provider { get; }
    Task<AiReviewResult> ReviewPipelineAsync(AiConnectionInfo conn, string prompt, CancellationToken ct);
}
