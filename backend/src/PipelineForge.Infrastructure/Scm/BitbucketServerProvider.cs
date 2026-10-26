using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PipelineForge.Core.Scm;
using PipelineForge.Infrastructure.Net;

namespace PipelineForge.Infrastructure.Scm;

/// <summary>Bitbucket Server / Data Center REST 1.0. Auth: HTTP access token (Bearer) or Basic.</summary>
public sealed class BitbucketServerProvider(IHttpClientFactory factory, IOptions<OutboundOptions> outbound)
    : ScmHttp(factory, outbound), ISourceControlProvider
{
    private const int MaxPages = 100;
    public ScmKind Kind => ScmKind.BitbucketServer;

    protected override void Authorize(HttpRequestMessage request, ScmConnectionInfo conn) => BasicOrBearer(request, conn);

    private static string Api(string project, string repo) => $"rest/api/1.0/projects/{Seg(project)}/repos/{Seg(repo)}";

    private async IAsyncEnumerable<JsonElement> Pages(ScmConnectionInfo conn, string relative, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var start = 0;
        var sep = relative.Contains('?') ? '&' : '?';
        for (var page = 0; page < MaxPages; page++)
        {
            using var doc = await GetJsonAsync(conn, Combine(conn, $"{relative}{sep}limit=1000&start={start}"), ct);
            var root = doc.RootElement;
            if (root.TryGetProperty("values", out var values))
                foreach (var v in values.EnumerateArray()) yield return v.Clone();
            if (!root.TryGetProperty("isLastPage", out var last) || last.GetBoolean()) yield break;
            if (!root.TryGetProperty("nextPageStart", out var next)) yield break;
            start = next.GetInt32();
        }
    }

    public async Task<IReadOnlyList<ScmRepository>> ListRepositoriesAsync(ScmConnectionInfo conn, string? query, CancellationToken ct)
    {
        var path = conn.Workspace is { Length: > 0 } project
            ? $"rest/api/1.0/projects/{Seg(project)}/repos"
            : "rest/api/1.0/repos" + (string.IsNullOrWhiteSpace(query) ? "" : "?name=" + Seg(query));
        var list = new List<ScmRepository>();
        await foreach (var r in Pages(conn, path, ct))
        {
            var key = Str(r, "project", "key") ?? "";
            var slug = Str(r, "slug") ?? "";
            var href = r.TryGetProperty("links", out var links) && links.TryGetProperty("self", out var self) && self.GetArrayLength() > 0
                ? Str(self[0], "href") : null;
            if (!string.IsNullOrWhiteSpace(query) && !slug.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(new ScmRepository($"{key}/{slug}", Str(r, "name") ?? slug, null, href, !(r.TryGetProperty("public", out var p) && p.GetBoolean())));
            if (list.Count >= 500) break;
        }
        return list;
    }

    public async Task<IReadOnlyList<ScmBranch>> ListBranchesAsync(ScmConnectionInfo conn, string repository, CancellationToken ct)
    {
        var (project, repo) = SplitRepo(repository);
        var list = new List<ScmBranch>();
        await foreach (var b in Pages(conn, $"{Api(project, repo)}/branches", ct))
            list.Add(new ScmBranch(Str(b, "displayId") ?? "", Str(b, "latestCommit")));
        return list;
    }

    public async Task<IReadOnlyList<string>> ListFilesAsync(ScmConnectionInfo conn, string repository, string branch, CancellationToken ct)
    {
        var (project, repo) = SplitRepo(repository);
        var files = new List<string>();
        await foreach (var f in Pages(conn, $"{Api(project, repo)}/files?at={Seg("refs/heads/" + branch)}", ct))
            if (f.ValueKind == JsonValueKind.String) files.Add(f.GetString()!);
        return files;
    }

    public async Task<string?> GetFileAsync(ScmConnectionInfo conn, string repository, string branch, string path, int maxBytes, CancellationToken ct)
    {
        var (project, repo) = SplitRepo(repository);
        using var request = new HttpRequestMessage(HttpMethod.Get, Combine(conn, $"{Api(project, repo)}/raw/{PathSegs(path)}?at={Seg("refs/heads/" + branch)}"));
        using var response = await SendAsync(conn, request, ct, allowNotFound: true);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        var bytes = await ReadLimitedAsync(response, maxBytes, ct);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    public async Task CreateBranchAsync(ScmConnectionInfo conn, string repository, string newBranch, string fromBranch, CancellationToken ct)
    {
        var (project, repo) = SplitRepo(repository);
        using var _ = await SendJsonAsync(conn, HttpMethod.Post,
            Combine(conn, $"rest/branch-utils/1.0/projects/{Seg(project)}/repos/{Seg(repo)}/branches"),
            new { name = newBranch, startPoint = "refs/heads/" + fromBranch }, ct);
    }

    private async Task<string?> HeadCommit(ScmConnectionInfo conn, string project, string repo, string branch, CancellationToken ct)
    {
        using var doc = await GetJsonAsync(conn, Combine(conn, $"{Api(project, repo)}/commits?until={Seg("refs/heads/" + branch)}&limit=1"), ct);
        return doc.RootElement.TryGetProperty("values", out var v) && v.GetArrayLength() > 0 ? Str(v[0], "id") : null;
    }

    /// <summary>The browse API commits one file at a time; existing files need the current head as sourceCommitId.</summary>
    public async Task CommitFilesAsync(ScmConnectionInfo conn, string repository, string branch, string message, IReadOnlyList<FileChange> files, CancellationToken ct)
    {
        var (project, repo) = SplitRepo(repository);
        var existing = (await ListFilesAsync(conn, repository, branch, ct)).ToHashSet(StringComparer.Ordinal);
        foreach (var f in files)
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent(f.Content), "content" },
                { new StringContent($"{message} ({f.Path})"), "message" },
                { new StringContent(branch), "branch" },
            };
            if (existing.Contains(f.Path) && await HeadCommit(conn, project, repo, branch, ct) is { } head)
                form.Add(new StringContent(head), "sourceCommitId");
            using var request = new HttpRequestMessage(HttpMethod.Put, Combine(conn, $"{Api(project, repo)}/browse/{PathSegs(f.Path)}")) { Content = form };
            using var _ = await SendAsync(conn, request, ct);
        }
    }

    public async Task<PullRequestResult> CreatePullRequestAsync(ScmConnectionInfo conn, PullRequestRequest pr, CancellationToken ct)
    {
        var (project, repo) = SplitRepo(pr.Repository);
        var repoRef = new { slug = repo, project = new { key = project } };
        using var doc = await SendJsonAsync(conn, HttpMethod.Post, Combine(conn, $"{Api(project, repo)}/pull-requests"), new
        {
            title = pr.Title,
            description = pr.Description,
            fromRef = new { id = "refs/heads/" + pr.SourceBranch, repository = repoRef },
            toRef = new { id = "refs/heads/" + pr.TargetBranch, repository = repoRef },
        }, ct);
        var root = doc.RootElement;
        var href = root.TryGetProperty("links", out var links) && links.TryGetProperty("self", out var self) && self.GetArrayLength() > 0
            ? Str(self[0], "href") : null;
        return new PullRequestResult(href ?? "", Str(root, "id") ?? "");
    }

    public async Task<string> WhoAmIAsync(ScmConnectionInfo conn, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Combine(conn, "rest/api/1.0/repos?limit=1"));
        using var response = await SendAsync(conn, request, ct);
        return response.Headers.TryGetValues("X-AUSERNAME", out var names) ? names.First() : "authenticated";
    }
}
