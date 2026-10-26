using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PipelineForge.Infrastructure.Persistence;
using PipelineForge.Infrastructure.Secrets;

namespace PipelineForge.Tests.Api;

/// <summary>Stands in for GitHub: records commits and pull requests instead of calling the API.</summary>
public sealed class FakeGitHub : PipelineForge.Core.Scm.ISourceControlProvider
{
    public readonly List<(string Repository, string Branch, string From)> Branches = [];
    public readonly List<(string Repository, IReadOnlyList<PipelineForge.Core.Scm.FileChange> Files)> Commits = [];
    public readonly List<PipelineForge.Core.Scm.PullRequestRequest> PullRequests = [];
    public PipelineForge.Core.Scm.ScmKind Kind => PipelineForge.Core.Scm.ScmKind.GitHub;
    public Task<IReadOnlyList<PipelineForge.Core.Scm.ScmRepository>> ListRepositoriesAsync(PipelineForge.Core.Scm.ScmConnectionInfo conn, string? query, CancellationToken ct) => Task.FromResult<IReadOnlyList<PipelineForge.Core.Scm.ScmRepository>>([]);
    public Task<IReadOnlyList<PipelineForge.Core.Scm.ScmBranch>> ListBranchesAsync(PipelineForge.Core.Scm.ScmConnectionInfo conn, string repository, CancellationToken ct) => Task.FromResult<IReadOnlyList<PipelineForge.Core.Scm.ScmBranch>>([]);
    public Task<IReadOnlyList<string>> ListFilesAsync(PipelineForge.Core.Scm.ScmConnectionInfo conn, string repository, string branch, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
    public Task<string?> GetFileAsync(PipelineForge.Core.Scm.ScmConnectionInfo conn, string repository, string branch, string path, int maxBytes, CancellationToken ct) => Task.FromResult<string?>(null);
    public Task CreateBranchAsync(PipelineForge.Core.Scm.ScmConnectionInfo conn, string repository, string newBranch, string fromBranch, CancellationToken ct)
    {
        lock (Branches) Branches.Add((repository, newBranch, fromBranch));
        return Task.CompletedTask;
    }
    public Task CommitFilesAsync(PipelineForge.Core.Scm.ScmConnectionInfo conn, string repository, string branch, string message, IReadOnlyList<PipelineForge.Core.Scm.FileChange> files, CancellationToken ct)
    {
        lock (Commits) Commits.Add((repository, files));
        return Task.CompletedTask;
    }
    public Task<PipelineForge.Core.Scm.PullRequestResult> CreatePullRequestAsync(PipelineForge.Core.Scm.ScmConnectionInfo conn, PipelineForge.Core.Scm.PullRequestRequest request, CancellationToken ct)
    {
        lock (PullRequests) PullRequests.Add(request);
        return Task.FromResult(new PipelineForge.Core.Scm.PullRequestResult($"https://github.com/{request.Repository}/pull/1", "1"));
    }
    public Task<IReadOnlyList<PipelineForge.Core.Scm.BranchRuleResult>> RequireBuildsAsync(PipelineForge.Core.Scm.ScmConnectionInfo conn, string repository, IReadOnlyList<string> branches, int approvals, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PipelineForge.Core.Scm.BranchRuleResult>>(branches.Select(b => new PipelineForge.Core.Scm.BranchRuleResult(b, "test", true, "Applied.")).ToList());
    public Task<string> WhoAmIAsync(PipelineForge.Core.Scm.ScmConnectionInfo conn, CancellationToken ct) => Task.FromResult("fake");
}

public sealed class MemorySecretStore : ISecretStore
{
    public readonly Dictionary<string, string> Values = [];
    public string Name => "memory";
    public Task<string> SetAsync(Guid workspaceId, string value, CancellationToken ct)
    {
        var reference = $"mem:{workspaceId:N}/{Guid.NewGuid():N}";
        Values[reference] = value;
        return Task.FromResult(reference);
    }
    public Task<string> GetAsync(string reference, CancellationToken ct) => Task.FromResult(Values[reference]);
    public Task DeleteAsync(string reference, CancellationToken ct) { Values.Remove(reference); return Task.CompletedTask; }
}

/// <summary>Authenticates "X-Test-User: alice" as subject alice with email alice@example.com.</summary>
public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var user)) return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new[]
        {
            new Claim("sub", user.ToString()), new Claim("iss", "https://test"),
            new Claim("email", $"{user}@example.com"), new Claim("name", user.ToString()),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Test")));
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public MemorySecretStore Secrets { get; } = new();
    public FakeGitHub GitHub { get; } = new();
    private readonly string _db = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Auth:Authority", "https://test");
        builder.ConfigureTestServices(services =>
        {
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_db));
            services.AddSingleton<ISecretStore>(Secrets);
            // ScmProviders picks the first provider of a kind, so this replaces the real GitHub client.
            services.Insert(0, ServiceDescriptor.Singleton<PipelineForge.Core.Scm.ISourceControlProvider>(GitHub));
            services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultScheme = "Test";
                o.DefaultAuthenticateScheme = "Test";
                o.DefaultChallengeScheme = "Test";
            });
        });
    }

    public HttpClient As(string user)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", user);
        return client;
    }

    public T Db<T>(Func<AppDbContext, T> f)
    {
        using var scope = Services.CreateScope();
        return f(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
