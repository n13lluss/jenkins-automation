using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using PipelineForge.Core.Scm;
using PipelineForge.Infrastructure.Net;
using PipelineForge.Infrastructure.Scm;

namespace PipelineForge.Tests.Core;

/// <summary>Records requests and answers them from a callback, so SCM calls can be checked without a network.</summary>
public sealed class RecordingHandler(Func<HttpRequestMessage, HttpStatusCode> answer, string body = "{}") : HttpMessageHandler
{
    public List<(HttpMethod Method, string Url, string Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add((request.Method, request.RequestUri!.ToString(), request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
        return new HttpResponseMessage(answer(request)) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}

public sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

public class BranchProtectionTests
{
    private static readonly IOptions<OutboundOptions> Outbound = Options.Create(new OutboundOptions());

    [Fact]
    public async Task GitHub_requires_the_jenkins_check_and_skips_wildcards()
    {
        var handler = new RecordingHandler(_ => HttpStatusCode.OK);
        var provider = new GitHubProvider(new SingleClientFactory(handler), Outbound);
        var conn = new ScmConnectionInfo(ScmKind.GitHub, new Uri("https://api.github.com"), null, "t", null);

        var results = await provider.RequireBuildsAsync(conn, "acme/shop", ["main", "release/*"], 2, CancellationToken.None);

        var put = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.Equal("https://api.github.com/repos/acme/shop/branches/main/protection", put.Url);
        Assert.Contains(GitHubProvider.JenkinsContext, put.Body);
        Assert.Contains("\"required_approving_review_count\":2", put.Body);
        Assert.Contains("\"allow_force_pushes\":false", put.Body);
        Assert.True(results.Single(r => r.Branch == "main").Ok);
        Assert.False(results.Single(r => r.Branch == "release/*").Ok);
    }

    [Fact]
    public async Task GitHub_reports_a_forbidden_token_per_branch()
    {
        var handler = new RecordingHandler(_ => HttpStatusCode.Forbidden);
        var provider = new GitHubProvider(new SingleClientFactory(handler), Outbound);
        var conn = new ScmConnectionInfo(ScmKind.GitHub, new Uri("https://api.github.com"), null, "t", null);

        var result = Assert.Single(await provider.RequireBuildsAsync(conn, "acme/shop", ["main"], 1, CancellationToken.None));
        Assert.False(result.Ok);
        Assert.Contains("permission", result.Message);
    }

    [Fact]
    public async Task Bitbucket_cloud_adds_missing_restrictions_only()
    {
        const string existing = """{"values":[{"kind":"force","pattern":"main"}]}""";
        var handler = new RecordingHandler(_ => HttpStatusCode.OK, existing);
        var provider = new BitbucketCloudProvider(new SingleClientFactory(handler), Outbound);
        var conn = new ScmConnectionInfo(ScmKind.BitbucketCloud, new Uri("https://api.bitbucket.org/2.0"), "bot", "t", "acme");

        var results = await provider.RequireBuildsAsync(conn, "acme/shop", ["main"], 1, CancellationToken.None);

        var posts = handler.Requests.Where(r => r.Method == HttpMethod.Post).ToList();
        Assert.Equal(2, posts.Count);
        Assert.All(posts, p => Assert.Equal("https://api.bitbucket.org/2.0/repositories/acme/shop/branch-restrictions", p.Url));
        Assert.Contains(posts, p => p.Body.Contains("require_passing_builds_to_merge"));
        Assert.Contains(posts, p => p.Body.Contains("require_approvals_to_merge"));
        Assert.Equal("Already set.", results.Single(r => r.Rule == "force").Message);
        Assert.All(results, r => Assert.True(r.Ok));
    }

    [Fact]
    public async Task Bitbucket_server_restricts_branches_and_enables_merge_checks()
    {
        var handler = new RecordingHandler(_ => HttpStatusCode.OK);
        var provider = new BitbucketServerProvider(new SingleClientFactory(handler), Outbound);
        var conn = new ScmConnectionInfo(ScmKind.BitbucketServer, new Uri("https://bitbucket.example.com"), null, "t", null);

        var results = await provider.RequireBuildsAsync(conn, "SHOP/orders", ["main", "release/*"], 1, CancellationToken.None);

        var restrictions = handler.Requests.Where(r => r.Url.Contains("branch-permissions/2.0")).ToList();
        Assert.Equal(6, restrictions.Count);
        Assert.Contains(restrictions, r => r.Body.Contains("refs/heads/main") && r.Body.Contains("\"BRANCH\""));
        Assert.Contains(restrictions, r => r.Body.Contains("release/*") && r.Body.Contains("\"PATTERN\""));
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Put && r.Url.Contains("requiredBuildsMergeCheck/enabled"));
        Assert.All(results, r => Assert.True(r.Ok));
    }

    [Fact]
    public async Task Malformed_repository_names_are_rejected()
    {
        var provider = new GitHubProvider(new SingleClientFactory(new RecordingHandler(_ => HttpStatusCode.OK)), Outbound);
        var conn = new ScmConnectionInfo(ScmKind.GitHub, new Uri("https://api.github.com"), null, "t", null);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => provider.RequireBuildsAsync(conn, "../../orgs/x", ["main"], 1, CancellationToken.None));
    }
}
