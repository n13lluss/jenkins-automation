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
    private readonly string _db = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Auth:Authority", "https://test");
        builder.ConfigureTestServices(services =>
        {
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_db));
            services.AddSingleton<ISecretStore>(Secrets);
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
