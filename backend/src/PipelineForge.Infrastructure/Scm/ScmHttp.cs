using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PipelineForge.Core.Scm;
using PipelineForge.Infrastructure.Net;

namespace PipelineForge.Infrastructure.Scm;

public sealed class ScmException(HttpStatusCode? status, string message) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>Shared HTTP plumbing: SSRF checks, auth headers, size limits, same-origin paging.</summary>
public abstract class ScmHttp(IHttpClientFactory factory, IOptions<OutboundOptions> outbound)
{
    public const string ClientName = "outbound";
    protected const int MaxJsonBytes = 10 * 1024 * 1024;
    private static readonly JsonDocumentOptions JsonOptions = new() { MaxDepth = 64 };

    protected HttpClient Http => factory.CreateClient(ClientName);

    protected abstract void Authorize(HttpRequestMessage request, ScmConnectionInfo conn);

    protected static string Seg(string value) => Uri.EscapeDataString(value);

    protected static string PathSegs(string path) => string.Join("/", path.Split('/').Select(Uri.EscapeDataString));

    protected static (string Owner, string Repo) SplitRepo(string repository)
    {
        var parts = repository.Split('/');
        if (parts.Length != 2 || parts.Any(p => p.Length == 0 || p.Length > 200 || p is "." or ".."))
            throw new ArgumentException("Repository must look like owner/name.");
        return (parts[0], parts[1]);
    }

    protected static Uri Combine(ScmConnectionInfo conn, string relative) =>
        new($"{conn.BaseUrl.ToString().TrimEnd('/')}/{relative.TrimStart('/')}");

    protected void BasicOrBearer(HttpRequestMessage request, ScmConnectionInfo conn)
    {
        request.Headers.Authorization = conn.Username is { Length: > 0 } user
            ? new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{conn.Token}")))
            : new AuthenticationHeaderValue("Bearer", conn.Token);
    }

    protected async Task<HttpResponseMessage> SendAsync(ScmConnectionInfo conn, HttpRequestMessage request, CancellationToken ct, bool allowNotFound = false)
    {
        OutboundGuard.ValidateUrl(request.RequestUri!, outbound.Value);
        EnsureSameOrigin(conn, request.RequestUri!);
        Authorize(request, conn);
        request.Headers.UserAgent.ParseAdd("PipelineForge/1.0");
        var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.IsSuccessStatusCode || (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)) return response;
        var status = response.StatusCode;
        response.Dispose();
        throw new ScmException(status, status switch
        {
            HttpStatusCode.Unauthorized => "The SCM rejected the credentials.",
            HttpStatusCode.Forbidden => "The token lacks permission for this operation.",
            HttpStatusCode.NotFound => "Repository, branch or file not found.",
            HttpStatusCode.Conflict => "The SCM reported a conflict (does the branch already exist?).",
            (HttpStatusCode)429 => "The SCM rate-limited the request.",
            _ when (int)status >= 300 && (int)status < 400 => "The SCM answered with a redirect; check the base URL.",
            _ => $"The SCM returned HTTP {(int)status}.",
        });
    }

    /// <summary>Paging links must stay on the connection's origin so a response cannot redirect our token elsewhere.</summary>
    protected static void EnsureSameOrigin(ScmConnectionInfo conn, Uri uri)
    {
        if (!string.Equals(uri.Scheme, conn.BaseUrl.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, conn.BaseUrl.Host, StringComparison.OrdinalIgnoreCase)
            || uri.Port != conn.BaseUrl.Port)
            throw new ScmException(null, "Refusing to follow a link to another host.");
    }

    protected async Task<JsonDocument> GetJsonAsync(ScmConnectionInfo conn, Uri uri, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await SendAsync(conn, request, ct);
        return await ReadJsonAsync(response, ct);
    }

    protected async Task<JsonDocument> SendJsonAsync(ScmConnectionInfo conn, HttpMethod method, Uri uri, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, uri)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await SendAsync(conn, request, ct);
        return await ReadJsonAsync(response, ct);
    }

    protected static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var bytes = await ReadLimitedAsync(response, MaxJsonBytes, ct) ?? throw new ScmException(null, "SCM response too large.");
        return bytes.Length == 0 ? JsonDocument.Parse("{}") : JsonDocument.Parse(bytes, JsonOptions);
    }

    /// <summary>Reads the body, or returns null when it exceeds <paramref name="maxBytes"/>.</summary>
    protected static async Task<byte[]?> ReadLimitedAsync(HttpResponseMessage response, int maxBytes, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > maxBytes) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    protected static string? Str(JsonElement e, params string[] path)
    {
        foreach (var p in path)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(p, out e)) return null;
        }
        return e.ValueKind == JsonValueKind.String ? e.GetString() : e.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False ? e.ToString() : null;
    }
}
