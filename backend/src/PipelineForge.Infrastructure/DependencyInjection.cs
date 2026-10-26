using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PipelineForge.Core.Ai;
using PipelineForge.Core.Model;
using PipelineForge.Core.Scm;
using PipelineForge.Infrastructure.Ai;
using PipelineForge.Infrastructure.Audit;
using PipelineForge.Infrastructure.Jenkins;
using PipelineForge.Infrastructure.Net;
using PipelineForge.Infrastructure.Persistence;
using PipelineForge.Infrastructure.Scm;
using PipelineForge.Infrastructure.Secrets;

namespace PipelineForge.Infrastructure;

public sealed class ScmProviders(IEnumerable<ISourceControlProvider> providers)
{
    public ISourceControlProvider Get(ScmKind kind) => providers.First(p => p.Kind == kind);
}

public sealed class AiProviders(IEnumerable<IAiReviewProvider> providers)
{
    public IAiReviewProvider Get(AiProvider kind) => providers.First(p => p.Provider == kind);
}

public static class DependencyInjection
{
    public static IServiceCollection AddPipelineForgeInfrastructure(this IServiceCollection services, IConfiguration config, bool addDatabase = true)
    {
        services.Configure<OutboundOptions>(config.GetSection("Outbound"));
        services.Configure<SecretStoreOptions>(config.GetSection("SecretStore"));

        if (addDatabase)
        {
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(
                config.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default is required.")));
        }

        services.AddHttpClient(ScmHttp.ClientName, c => c.Timeout = TimeSpan.FromSeconds(60))
            .ConfigurePrimaryHttpMessageHandler(sp => OutboundGuard.CreateHandler(sp.GetRequiredService<IOptions<OutboundOptions>>().Value));

        services.AddSingleton<ISourceControlProvider, BitbucketCloudProvider>();
        services.AddSingleton<ISourceControlProvider, BitbucketServerProvider>();
        services.AddSingleton<ISourceControlProvider, GitHubProvider>();
        services.AddSingleton<ScmProviders>();

        services.AddSingleton<IAiReviewProvider, AnthropicReviewProvider>();
        services.AddSingleton<IAiReviewProvider>(sp => new OpenAiCompatibleReviewProvider(
            sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<IOptions<OutboundOptions>>(), AiProvider.OpenAiCompatible));
        services.AddSingleton<IAiReviewProvider>(sp => new OpenAiCompatibleReviewProvider(
            sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<IOptions<OutboundOptions>>(), AiProvider.AzureOpenAi));
        services.AddSingleton<AiProviders>();

        services.AddSingleton<JenkinsClient>();
        services.AddScoped<IAuditLog, AuditLog>();

        services.AddSingleton<ISecretStore>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<SecretStoreOptions>>();
            return o.Value.Provider switch
            {
                "vault" => new VaultSecretStore(o),
                "azure-key-vault" => new AzureKeyVaultSecretStore(o),
                var other => throw new InvalidOperationException($"Unknown secret store '{other}'. Use vault or azure-key-vault."),
            };
        });
        return services;
    }
}
