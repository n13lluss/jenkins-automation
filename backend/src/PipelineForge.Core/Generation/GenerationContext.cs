using System.Text;
using PipelineForge.Core.Model;
using static PipelineForge.Core.Validation.GroovyEscaper;

namespace PipelineForge.Core.Generation;

public enum SonarMode { Maven, DotNet, Cli }

/// <summary>Derived facts about a validated config that several writers need.</summary>
public sealed class GenerationContext
{
    public PipelineConfig Config { get; }
    public bool IsKubernetes => Config.Agent.Type == AgentType.Kubernetes;
    public bool Library => Config.Layout == PipelineLayout.SharedLibrary;
    /// <summary>Global variable of the pipeline in the shared library, e.g. pfOrdersApi for "orders-api".</summary>
    public string LibraryVar => "pf" + string.Concat(Config.Name.Split('-', '.', '_').Where(p => p.Length > 0).Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    /// <summary>Folder under the library's resources/ holding this pipeline's support files.</summary>
    public string ResourceRoot => $"pipelineforge/{Config.Name}";
    public IReadOnlyList<ModuleConfig> ImageModules { get; }
    public IReadOnlyList<EnvironmentConfig> DeployEnvironments { get; }
    public SonarMode SonarMode { get; }
    public bool UsesTrivy { get; }
    public bool HasDependencyStage => Config.Security.DependencyScanners.Count > 0;
    public bool HasDast => Config.Security.Dast.Enabled && Config.Security.Dast.Environments.Count > 0;
    public bool IsIso => Config.Compliance.Profile == ComplianceProfile.Iso27001;
    public bool Signing => Config.Signing.Enabled && ImageModules.Count > 0;
    public bool Attestations => Signing && Config.Signing.Attestations;
    public bool DockerfileLint => Config.Security.DockerfileLint && ImageModules.Count > 0;
    public bool ClusterPolicy => Config.ClusterPolicy.Enabled && Signing && DeployEnvironments.Any(e => e.Deploy.Type == DeployTargetType.Helm);
    /// <summary>Deploy environments that build their own image (no PromoteFrom).</summary>
    public IReadOnlyList<EnvironmentConfig> BuildEnvironments => DeployEnvironments.Where(e => e.PromoteFrom is null).ToList();
    /// <summary>Environment keys other environments promote from; their deploys tag the running digest.</summary>
    public IReadOnlySet<string> PromotionSources => DeployEnvironments.Where(e => e.PromoteFrom is not null).Select(e => e.PromoteFrom!).ToHashSet();
    public bool Promotion => PromotionSources.Count > 0;
    public bool Migrations => Config.Migrations.Tool is MigrationTool.Flyway or MigrationTool.Liquibase && DeployEnvironments.Any(e => e.Database is not null);
    public bool Exceptions => Config.Security.ExceptionRegister;
    /// <summary>Post-deploy scans that read ignore lists and so need the rendered exception files.</summary>
    public bool ExceptionsOnDeployAgents => Exceptions && (HasDast || Scheduled || Authenticated);
    public bool Ephemeral => Config.Security.Dast.Ephemeral.Enabled && ImageModules.Count > 0;
    public bool Authenticated => Config.Security.Dast.Authenticated.Enabled;
    public bool Scheduled => Config.Security.Dast.Schedule.Enabled;
    public bool Ticketing => Config.Ticketing.Provider != TicketProvider.None;
    public bool RateLimit => Config.RateLimit.Enabled;
    public bool Pinning => Config.Security.DependencyPinning && Config.Modules.Any(m => m.Ecosystem == Ecosystem.Maven);
    public bool SnykMonitor => Config.Security.SnykMonitor && DeployEnvironments.Count > 0;
    public bool UsesSnyk => Config.Security.DependencyScanners.Contains(DependencyScanner.Snyk)
                            || (Config.Security.ContainerScan && Config.Security.ContainerScanner == ScannerTool.Snyk && ImageModules.Count > 0)
                            || (Config.Security.IacScan && Config.Security.IacScanner == ScannerTool.Snyk);
    public bool TicketsFrom(TicketSource s) => Ticketing && Config.Ticketing.Sources.Contains(s);
    public DependencyProxyConfig? Proxy => Config.DependencyProxy;

    /// <summary>The image scanned by the ephemeral DAST stage.</summary>
    public ModuleConfig? EphemeralModule =>
        ImageModules.FirstOrDefault(m => m.Name == Config.Security.Dast.Ephemeral.Module) ?? ImageModules.FirstOrDefault();

    /// <summary>Environment variable carrying a module's pushed image digest between stages.</summary>
    public static string DigestVar(ModuleConfig m) =>
        "IMAGE_DIGEST_" + new string(m.Name.ToUpperInvariant().Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '_').ToArray());

    /// <summary>Containers for the CI agent, name → image, in a stable order.</summary>
    public IReadOnlyDictionary<string, string> CiContainers { get; }
    /// <summary>Containers for deploy/DAST agents, name → image.</summary>
    public IReadOnlyDictionary<string, string> DeployContainers { get; }

    public GenerationContext(PipelineConfig config)
    {
        Config = config;
        ImageModules = config.Modules.Where(m => m.Dockerfile is not null).ToList();
        DeployEnvironments = config.Environments.Where(e => e.Deploy.Type != DeployTargetType.None).ToList();
        SonarMode = config.Modules.Count == 1
            ? config.Modules[0].Ecosystem switch
            {
                Ecosystem.Maven => SonarMode.Maven,
                Ecosystem.DotNet => SonarMode.DotNet,
                _ => SonarMode.Cli,
            }
            : SonarMode.Cli;
        var s = config.Security;
        UsesTrivy = s.DependencyScanners.Contains(DependencyScanner.TrivyFs) || s.Sbom || s.LicenseCheck
                    || (s.IacScan && s.IacScanner == ScannerTool.Trivy)
                    || (s.ContainerScan && s.ContainerScanner == ScannerTool.Trivy && ImageModules.Count > 0);

        var ci = new Dictionary<string, string>();
        foreach (var m in config.Modules) ci.TryAdd(ToolImages.ContainerName(m), ToolImages.EcosystemImage(m));
        void Tool(Dictionary<string, string> into, string name) => into.TryAdd(name, Image(name));
        if (s.SecretScan) Tool(ci, "gitleaks");
        if (UsesTrivy) Tool(ci, "trivy");
        if (s.DependencyScanners.Contains(DependencyScanner.OwaspDependencyCheck)) Tool(ci, "dependency-check");
        if (s.DependencyScanners.Contains(DependencyScanner.OsvScanner)) Tool(ci, "osv-scanner");
        if (UsesSnyk) Tool(ci, "snyk");
        if (config.Quality.Enabled && SonarMode == SonarMode.Cli) Tool(ci, "sonar-scanner");
        if (ImageModules.Count > 0 && IsKubernetes) { Tool(ci, "kaniko"); Tool(ci, "crane"); }
        if (Signing) Tool(ci, "cosign");
        if (Attestations) { Tool(ci, "trivy"); Tool(ci, "python"); }
        if (DockerfileLint) Tool(ci, "hadolint");
        if (config.AiReview.Enabled || Ticketing || Exceptions) Tool(ci, "python");
        if (TicketsFrom(TicketSource.Sonar) && config.Quality.Enabled) Tool(ci, "curl");
        CiContainers = ci;

        var deploy = new Dictionary<string, string>();
        foreach (var e in DeployEnvironments)
        {
            switch (e.Deploy.Type)
            {
                case DeployTargetType.Helm: Tool(deploy, "helm"); break;
                case DeployTargetType.DockerSsh: Tool(deploy, "ssh"); break;
                case DeployTargetType.AzureAppService: Tool(deploy, "azure-cli"); break;
            }
            if (e.Url is not null) Tool(deploy, "curl");
        }
        if (HasDast || Authenticated || Scheduled) Tool(deploy, "zap");
        if (Signing && DeployEnvironments.Count > 0) Tool(deploy, "cosign");
        if (SnykMonitor) Tool(deploy, "snyk");
        if (Promotion) Tool(deploy, "crane");
        if (Migrations) Tool(deploy, Config.Migrations.Tool == MigrationTool.Flyway ? "flyway" : "liquibase");
        if (RateLimit || Authenticated || Ticketing || ExceptionsOnDeployAgents) Tool(deploy, "python");
        DeployContainers = deploy;
    }

    public string Image(string tool)
    {
        if (Config.Agent.ImageOverrides.TryGetValue(tool, out var o)) return o;
        // Snyk needs the build tool to resolve Maven dependency trees.
        if (tool == "snyk" && Config.Modules.Any(m => m.Ecosystem == Ecosystem.Maven)) return ToolImages.Tools["snyk-maven"];
        return ToolImages.Tools[tool];
    }

    /// <summary>Declarative <c>when</c> condition for a stage group, or null when it always runs.</summary>
    public string? TriggerCondition(StageTrigger trigger) => trigger switch
    {
        StageTrigger.PullRequests => "changeRequest()",
        StageTrigger.DeployBranches when DeployEnvironments.Count > 0 => DeployBranchesCondition(),
        _ => null,
    };

    public IEnumerable<KeyValuePair<string, string>> AllContainers =>
        CiContainers.Concat(DeployContainers.Where(d => !CiContainers.ContainsKey(d.Key)));

    /// <summary>Declarative <c>when</c> condition for one branch glob.</summary>
    public static string BranchCondition(string pattern) =>
        $"branch pattern: {Quote(pattern)}, comparator: 'GLOB'";

    /// <summary>Condition matching any branch that builds and pushes an image (environments without PromoteFrom).</summary>
    public string DeployBranchesCondition() => BranchesCondition(BuildEnvironments.Count > 0 ? BuildEnvironments : DeployEnvironments);

    private static string BranchesCondition(IEnumerable<EnvironmentConfig> envs)
    {
        var patterns = envs.Select(e => e.BranchPattern).Distinct().ToList();
        if (patterns.Count == 1) return BranchCondition(patterns[0]);
        var sb = new StringBuilder("anyOf { ");
        sb.Append(string.Join("; ", patterns.Select(BranchCondition)));
        return sb.Append(" }").ToString();
    }

    public static string CvssThreshold(Severity s) => s switch
    {
        Severity.Low => "0.1",
        Severity.Medium => "4.0",
        Severity.High => "7.0",
        _ => "9.0",
    };

    /// <summary>Trivy/Snyk style severity list at or above the threshold.</summary>
    public static string SeveritiesFrom(Severity s) =>
        string.Join(",", Enum.GetValues<Severity>().Where(x => x >= s).Select(x => x.ToString().ToUpperInvariant()));
}
