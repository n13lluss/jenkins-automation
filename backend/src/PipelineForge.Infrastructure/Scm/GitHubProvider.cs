using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PipelineForge.Core.Scm;
using PipelineForge.Infrastructure.Net;

namespace PipelineForge.Infrastructure.Scm;

/// <summary>GitHub / GitHub Enterprise REST v3 with a fine-grained personal access token.</summary>
public sealed class GitHubProvider(IHttpClientFactory factory, IOptions<OutboundOptions> outbound)
    : ScmHttp(factory, outbound), ISourceControlProvider
{
    public ScmKind Kind => ScmKind.GitHub;

    protected override void Authorize(HttpRequestMessage request, ScmConnectionInfo conn)
    {
        request.Headers.Authorization = new("Bearer", conn.Token);
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
    }

    private static string Repo(string repository)
    {
        var (owner, name) = SplitRepo(repository);
        return $"repos/{Seg(owner)}/{Seg(name)}";
    }

    public async Task<IReadOnlyList<ScmRepository>> ListRepositoriesAsync(ScmConnectionInfo conn, string? query, CancellationToken ct)
    {
        var list = new List<ScmRepository>();
        for (var page = 1; page <= 5; page++)
        {
            var path = conn.Workspace is { Length: > 0 } org
                ? $"orgs/{Seg(org)}/repos?per_page=100&sort=updated&page={page}"
                : $"user/repos?per_page=100&sort=updated&affiliation=owner,collaborator,organization_member&page={page}";
            using var doc = await GetJsonAsync(conn, Combine(conn, path), ct);
            var count = 0;
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                count++;
                var full = Str(r, "full_name") ?? "";
                if (!string.IsNullOrWhiteSpace(query) && !full.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new ScmRepository(full, Str(r, "name") ?? "", Str(r, "default_branch"), Str(r, "html_url"),
                    r.TryGetProperty("private", out var p) && p.GetBoolean()));
            }
            if (count < 100) break;
        }
        return list;
    }

    public async Task<IReadOnlyList<ScmBranch>> ListBranchesAsync(ScmConnectionInfo conn, string repository, CancellationToken ct)
    {
        var list = new List<ScmBranch>();
        for (var page = 1; page <= 10; page++)
        {
            using var doc = await GetJsonAsync(conn, Combine(conn, $"{Repo(repository)}/branches?per_page=100&page={page}"), ct);
            var count = 0;
            foreach (var b in doc.RootElement.EnumerateArray())
            {
                count++;
                list.Add(new ScmBranch(Str(b, "name") ?? "", Str(b, "commit", "sha")));
            }
            if (count < 100) break;
        }
        return list;
    }

    private async Task<string> BranchSha(ScmConnectionInfo conn, string repository, string branch, CancellationToken ct)
    {
        using var doc = await GetJsonAsync(conn, Combine(conn, $"{Repo(repository)}/git/ref/heads/{PathSegs(branch)}"), ct);
        return Str(doc.RootElement, "object", "sha") ?? throw new ScmException(null, "Branch not found.");
    }

    public async Task<IReadOnlyList<string>> ListFilesAsync(ScmConnectionInfo conn, string repository, string branch, CancellationToken ct)
    {
        var sha = await BranchSha(conn, repository, branch, ct);
        using var doc = await GetJsonAsync(conn, Combine(conn, $"{Repo(repository)}/git/trees/{Seg(sha)}?recursive=1"), ct);
        var files = doc.RootElement.GetProperty("tree").EnumerateArray()
            .Where(e => Str(e, "type") == "blob")
            .Select(e => Str(e, "path")!)
            .ToList();
        return files;
    }

    public async Task<string?> GetFileAsync(ScmConnectionInfo conn, string repository, string branch, string path, int maxBytes, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Combine(conn, $"{Repo(repository)}/contents/{PathSegs(path)}?ref={Seg(branch)}"));
        request.Headers.Accept.ParseAdd("application/vnd.github.raw+json");
        using var response = await SendAsync(conn, request, ct, allowNotFound: true);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        var bytes = await ReadLimitedAsync(response, maxBytes, ct);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    public async Task CreateBranchAsync(ScmConnectionInfo conn, string repository, string newBranch, string fromBranch, CancellationToken ct)
    {
        var sha = await BranchSha(conn, repository, fromBranch, ct);
        using var _ = await SendJsonAsync(conn, HttpMethod.Post, Combine(conn, $"{Repo(repository)}/git/refs"),
            new { @ref = "refs/heads/" + newBranch, sha }, ct);
    }

    /// <summary>One commit for all files via the Git Data API (tree → commit → ref update).</summary>
    public async Task CommitFilesAsync(ScmConnectionInfo conn, string repository, string branch, string message, IReadOnlyList<FileChange> files, CancellationToken ct)
    {
        var head = await BranchSha(conn, repository, branch, ct);
        string baseTree;
        using (var commit = await GetJsonAsync(conn, Combine(conn, $"{Repo(repository)}/git/commits/{Seg(head)}"), ct))
            baseTree = Str(commit.RootElement, "tree", "sha")!;
        string tree;
        using (var t = await SendJsonAsync(conn, HttpMethod.Post, Combine(conn, $"{Repo(repository)}/git/trees"), new
               {
                   base_tree = baseTree,
                   tree = files.Select(f => new { path = f.Path, mode = f.Path.EndsWith(".sh") || f.Path.EndsWith(".py") ? "100755" : "100644", type = "blob", content = f.Content }),
               }, ct))
            tree = Str(t.RootElement, "sha")!;
        string newCommit;
        using (var c = await SendJsonAsync(conn, HttpMethod.Post, Combine(conn, $"{Repo(repository)}/git/commits"),
                   new { message, tree, parents = new[] { head } }, ct))
            newCommit = Str(c.RootElement, "sha")!;
        using var _ = await SendJsonAsync(conn, HttpMethod.Patch, Combine(conn, $"{Repo(repository)}/git/refs/heads/{PathSegs(branch)}"),
            new { sha = newCommit, force = false }, ct);
    }

    public async Task<PullRequestResult> CreatePullRequestAsync(ScmConnectionInfo conn, PullRequestRequest pr, CancellationToken ct)
    {
        using var doc = await SendJsonAsync(conn, HttpMethod.Post, Combine(conn, $"{Repo(pr.Repository)}/pulls"),
            new { title = pr.Title, head = pr.SourceBranch, @base = pr.TargetBranch, body = pr.Description }, ct);
        return new PullRequestResult(Str(doc.RootElement, "html_url") ?? "", Str(doc.RootElement, "number") ?? "");
    }

    /// <summary>Jenkins (GitHub Branch Source) reports this status context for PR merge builds.</summary>
    public const string JenkinsContext = "continuous-integration/jenkins/pr-merge";

    public async Task<IReadOnlyList<BranchRuleResult>> RequireBuildsAsync(ScmConnectionInfo conn, string repository, IReadOnlyList<string> branches, int approvals, CancellationToken ct)
    {
        var results = new List<BranchRuleResult>();
        foreach (var branch in branches)
        {
            if (branch.Contains('*'))
            {
                results.Add(new(branch, "branch protection", false, "Wildcard branches need a GitHub ruleset; set it up in the repository settings."));
                continue;
            }
            try
            {
                using var _ = await SendJsonAsync(conn, HttpMethod.Put, Combine(conn, $"{Repo(repository)}/branches/{PathSegs(branch)}/protection"), new
                {
                    required_status_checks = new { strict = true, contexts = new[] { JenkinsContext } },
                    enforce_admins = true,
                    required_pull_request_reviews = new { required_approving_review_count = approvals, dismiss_stale_reviews = true },
                    restrictions = (object?)null,
                    allow_force_pushes = false,
                    allow_deletions = false,
                }, ct);
                results.Add(new(branch, "required Jenkins check + approvals + no force push", true, "Applied."));
            }
            catch (ScmException e)
            {
                results.Add(new(branch, "branch protection", false, e.Message));
            }
        }
        return results;
    }

    public async Task<string> WhoAmIAsync(ScmConnectionInfo conn, CancellationToken ct)
    {
        using var doc = await GetJsonAsync(conn, Combine(conn, "user"), ct);
        return Str(doc.RootElement, "login") ?? "authenticated";
    }
}
