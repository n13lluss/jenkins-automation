using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PipelineForge.Core.Scm;
using PipelineForge.Infrastructure.Net;

namespace PipelineForge.Infrastructure.Scm;

/// <summary>Bitbucket Cloud REST 2.0. Auth: API token / app password (Basic) or access token (Bearer).</summary>
public sealed class BitbucketCloudProvider(IHttpClientFactory factory, IOptions<OutboundOptions> outbound)
    : ScmHttp(factory, outbound), ISourceControlProvider
{
    private const int MaxPages = 100;
    public ScmKind Kind => ScmKind.BitbucketCloud;

    protected override void Authorize(HttpRequestMessage request, ScmConnectionInfo conn) => BasicOrBearer(request, conn);

    private async IAsyncEnumerable<JsonElement> Pages(ScmConnectionInfo conn, Uri first, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        Uri? next = first;
        for (var page = 0; next is not null && page < MaxPages; page++)
        {
            using var doc = await GetJsonAsync(conn, next, ct);
            if (doc.RootElement.TryGetProperty("values", out var values))
                foreach (var v in values.EnumerateArray()) yield return v.Clone();
            next = Str(doc.RootElement, "next") is { } n ? new Uri(n) : null;
        }
    }

    public async Task<IReadOnlyList<ScmRepository>> ListRepositoriesAsync(ScmConnectionInfo conn, string? query, CancellationToken ct)
    {
        var q = string.IsNullOrWhiteSpace(query) ? "" : "&q=" + Seg($"name ~ \"{query.Replace("\"", "")}\"");
        var url = conn.Workspace is { Length: > 0 } ws
            ? Combine(conn, $"repositories/{Seg(ws)}?pagelen=100&sort=-updated_on{q}")
            : Combine(conn, $"repositories?role=member&pagelen=100&sort=-updated_on{q}");
        var list = new List<ScmRepository>();
        await foreach (var r in Pages(conn, url, ct))
        {
            list.Add(new ScmRepository(Str(r, "full_name") ?? "", Str(r, "name") ?? "", Str(r, "mainbranch", "name"),
                Str(r, "links", "html", "href"), r.TryGetProperty("is_private", out var p) && p.GetBoolean()));
            if (list.Count >= 500) break;
        }
        return list;
    }

    public async Task<IReadOnlyList<ScmBranch>> ListBranchesAsync(ScmConnectionInfo conn, string repository, CancellationToken ct)
    {
        var (ws, slug) = SplitRepo(repository);
        var list = new List<ScmBranch>();
        await foreach (var b in Pages(conn, Combine(conn, $"repositories/{Seg(ws)}/{Seg(slug)}/refs/branches?pagelen=100"), ct))
            list.Add(new ScmBranch(Str(b, "name") ?? "", Str(b, "target", "hash")));
        return list;
    }

    private async Task<string> CommitOf(ScmConnectionInfo conn, string ws, string slug, string branch, CancellationToken ct)
    {
        using var doc = await GetJsonAsync(conn, Combine(conn, $"repositories/{Seg(ws)}/{Seg(slug)}/refs/branches/{Seg(branch)}"), ct);
        return Str(doc.RootElement, "target", "hash") ?? throw new ScmException(null, "Branch has no commit.");
    }

    public async Task<IReadOnlyList<string>> ListFilesAsync(ScmConnectionInfo conn, string repository, string branch, CancellationToken ct)
    {
        var (ws, slug) = SplitRepo(repository);
        var commit = await CommitOf(conn, ws, slug, branch, ct);
        var files = new List<string>();
        await foreach (var e in Pages(conn, Combine(conn, $"repositories/{Seg(ws)}/{Seg(slug)}/src/{Seg(commit)}/?max_depth=30&pagelen=100"), ct))
            if (Str(e, "type") == "commit_file" && Str(e, "path") is { } path) files.Add(path);
        return files;
    }

    public async Task<string?> GetFileAsync(ScmConnectionInfo conn, string repository, string branch, string path, int maxBytes, CancellationToken ct)
    {
        var (ws, slug) = SplitRepo(repository);
        var commit = await CommitOf(conn, ws, slug, branch, ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, Combine(conn, $"repositories/{Seg(ws)}/{Seg(slug)}/src/{Seg(commit)}/{PathSegs(path)}"));
        using var response = await SendAsync(conn, request, ct, allowNotFound: true);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        var bytes = await ReadLimitedAsync(response, maxBytes, ct);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    public async Task CreateBranchAsync(ScmConnectionInfo conn, string repository, string newBranch, string fromBranch, CancellationToken ct)
    {
        var (ws, slug) = SplitRepo(repository);
        var commit = await CommitOf(conn, ws, slug, fromBranch, ct);
        using var _ = await SendJsonAsync(conn, HttpMethod.Post, Combine(conn, $"repositories/{Seg(ws)}/{Seg(slug)}/refs/branches"),
            new { name = newBranch, target = new { hash = commit } }, ct);
    }

    public async Task CommitFilesAsync(ScmConnectionInfo conn, string repository, string branch, string message, IReadOnlyList<FileChange> files, CancellationToken ct)
    {
        var (ws, slug) = SplitRepo(repository);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(message), "message" },
            { new StringContent(branch), "branch" },
        };
        foreach (var f in files) form.Add(new StringContent(f.Content), f.Path);
        using var request = new HttpRequestMessage(HttpMethod.Post, Combine(conn, $"repositories/{Seg(ws)}/{Seg(slug)}/src")) { Content = form };
        using var _ = await SendAsync(conn, request, ct);
    }

    public async Task<PullRequestResult> CreatePullRequestAsync(ScmConnectionInfo conn, PullRequestRequest pr, CancellationToken ct)
    {
        var (ws, slug) = SplitRepo(pr.Repository);
        using var doc = await SendJsonAsync(conn, HttpMethod.Post, Combine(conn, $"repositories/{Seg(ws)}/{Seg(slug)}/pullrequests"), new
        {
            title = pr.Title,
            description = pr.Description,
            source = new { branch = new { name = pr.SourceBranch } },
            destination = new { branch = new { name = pr.TargetBranch } },
            close_source_branch = true,
        }, ct);
        return new PullRequestResult(Str(doc.RootElement, "links", "html", "href") ?? "", Str(doc.RootElement, "id") ?? "");
    }

    public async Task<IReadOnlyList<BranchRuleResult>> RequireBuildsAsync(ScmConnectionInfo conn, string repository, IReadOnlyList<string> branches, int approvals, CancellationToken ct)
    {
        var (ws, slug) = SplitRepo(repository);
        var url = Combine(conn, $"repositories/{Seg(ws)}/{Seg(slug)}/branch-restrictions");
        var existing = new HashSet<(string Kind, string Pattern)>();
        await foreach (var r in Pages(conn, Combine(conn, $"repositories/{Seg(ws)}/{Seg(slug)}/branch-restrictions?pagelen=100"), ct))
            existing.Add((Str(r, "kind") ?? "", Str(r, "pattern") ?? ""));
        var results = new List<BranchRuleResult>();
        foreach (var branch in branches)
        {
            foreach (var (kind, value) in new (string, int?)[] { ("require_passing_builds_to_merge", 1), ("require_approvals_to_merge", approvals), ("force", null) })
            {
                if (existing.Contains((kind, branch)))
                {
                    results.Add(new(branch, kind, true, "Already set."));
                    continue;
                }
                try
                {
                    object body = value is { } v
                        ? new { kind, branch_match_kind = "glob", pattern = branch, value = v }
                        : new { kind, branch_match_kind = "glob", pattern = branch };
                    using var _ = await SendJsonAsync(conn, HttpMethod.Post, url, body, ct);
                    results.Add(new(branch, kind, true, "Applied."));
                }
                catch (ScmException e)
                {
                    results.Add(new(branch, kind, false, e.Message));
                }
            }
        }
        return results;
    }

    public async Task<string> WhoAmIAsync(ScmConnectionInfo conn, CancellationToken ct)
    {
        if (conn.Workspace is { Length: > 0 } ws)
        {
            using var doc = await GetJsonAsync(conn, Combine(conn, $"repositories/{Seg(ws)}?pagelen=1"), ct);
            return $"workspace {ws}";
        }
        using var user = await GetJsonAsync(conn, Combine(conn, "user"), ct);
        return Str(user.RootElement, "display_name") ?? Str(user.RootElement, "username") ?? "authenticated";
    }
}
