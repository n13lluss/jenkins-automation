using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PipelineForge.Core.Ai;
using PipelineForge.Core.Model;
using PipelineForge.Infrastructure.Net;
using PipelineForge.Infrastructure.Scm;

namespace PipelineForge.Infrastructure.Ai;

/// <summary>Any OpenAI-compatible chat completions API, or Azure OpenAI.</summary>
public sealed class OpenAiCompatibleReviewProvider(IHttpClientFactory factory, IOptions<OutboundOptions> outbound, AiProvider provider) : IAiReviewProvider
{
    public AiProvider Provider => provider;

    public async Task<AiReviewResult> ReviewPipelineAsync(AiConnectionInfo conn, string prompt, CancellationToken ct)
    {
        var hash = AiReviewPrompt.Sha256(prompt);
        Uri url;
        if (provider == AiProvider.AzureOpenAi)
        {
            var b = conn.BaseUrl ?? throw new ArgumentException("Azure OpenAI needs the resource URL.");
            url = new Uri($"{b.ToString().TrimEnd('/')}/openai/deployments/{Uri.EscapeDataString(conn.Model)}/chat/completions?api-version=2024-10-21");
        }
        else
        {
            url = new Uri($"{(conn.BaseUrl ?? new Uri("https://api.openai.com")).ToString().TrimEnd('/')}/v1/chat/completions");
        }
        OutboundGuard.ValidateUrl(url, outbound.Value);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new
            {
                model = conn.Model,
                messages = new object[]
                {
                    new { role = "system", content = AiReviewPrompt.System + " JSON schema: " + AiReviewPrompt.SchemaJson },
                    new { role = "user", content = prompt },
                },
                response_format = new { type = "json_object" },
            }),
        };
        if (provider == AiProvider.AzureOpenAi) request.Headers.TryAddWithoutValidation("api-key", conn.ApiKey);
        else request.Headers.Authorization = new("Bearer", conn.ApiKey);

        using var http = factory.CreateClient(ScmHttp.ClientName);
        http.Timeout = TimeSpan.FromMinutes(5);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return new("", [], $"The AI provider returned HTTP {(int)response.StatusCode}.", hash);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct), new JsonDocumentOptions { MaxDepth = 32 });
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        return AiReviewPrompt.Parse(content, hash);
    }
}
