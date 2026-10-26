using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Options;
using PipelineForge.Core.Ai;
using PipelineForge.Core.Model;
using PipelineForge.Infrastructure.Net;
using PipelineForge.Infrastructure.Scm;

namespace PipelineForge.Infrastructure.Ai;

/// <summary>Claude via the official Anthropic SDK, through the SSRF-guarded HTTP client.</summary>
public sealed class AnthropicReviewProvider(IHttpClientFactory factory, IOptions<OutboundOptions> outbound) : IAiReviewProvider
{
    public AiProvider Provider => AiProvider.Anthropic;

    public async Task<AiReviewResult> ReviewPipelineAsync(AiConnectionInfo conn, string prompt, CancellationToken ct)
    {
        var hash = AiReviewPrompt.Sha256(prompt);
        var baseUrl = conn.BaseUrl ?? new Uri("https://api.anthropic.com");
        OutboundGuard.ValidateUrl(baseUrl, outbound.Value);
        var client = new AnthropicClient
        {
            ApiKey = conn.ApiKey,
            BaseUrl = baseUrl.ToString().TrimEnd('/'),
            HttpClient = factory.CreateClient(ScmHttp.ClientName),
            Timeout = TimeSpan.FromMinutes(5),
        };
        try
        {
            var response = await client.Beta.Messages.Create(new MessageCreateParams
            {
                Model = conn.Model,
                MaxTokens = 16000,
                System = AiReviewPrompt.System,
                Messages = [new BetaUserMessageParam { Content = prompt }],
                OutputConfig = new BetaOutputConfig
                {
                    Effort = Effort.High,
                    Format = new BetaJsonOutputFormat { Schema = AiReviewPrompt.Schema },
                },
                // Server-side refusal fallback; models without fallback support simply ignore "default".
                Betas = ["server-side-fallback-2026-07-01"],
                Fallbacks = new Default(),
            }, ct);
            if (response.StopReason == "refusal") return new("", [], "The model declined to review this pipeline.", hash);
            if (response.StopReason == "max_tokens") return new("", [], "The AI response was cut off.", hash);
            var text = string.Concat(response.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));
            return AiReviewPrompt.Parse(text, hash);
        }
        catch (AnthropicRateLimitException)
        {
            return new("", [], "The AI provider rate-limited the request. Try again later.", hash);
        }
        catch (AnthropicApiException e)
        {
            return new("", [], $"The AI provider returned an error ({e.GetType().Name}).", hash);
        }
    }
}
