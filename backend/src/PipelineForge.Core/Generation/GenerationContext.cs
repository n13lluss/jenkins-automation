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
    public IReadOnlyList<ModuleConfig> ImageModules { get; }
    public IReadOnlyList<EnvironmentConfig> DeployEnvironments { get; }
    public SonarMode SonarMode { get; }
    public bool UsesTrivy { get; }
    public bool HasDependencyStage => Config.Security.DependencyScanners.Count > 0;
    public bool HasDast => Config.Security.Dast.Enabled && Config.Security.Dast.Environments.Count > 0;
    public bool IsIso => Config.Compliance.Profile == ComplianceProfile.Iso27001;

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
        UsesTrivy = s.DependencyScanners.Contains(DependencyScanner.TrivyFs) || s.Sbom || s.LicenseCheck || s.IacScan
                    || (s.ContainerScan && ImageModules.Count > 0);

        var ci = new Dictionary<string, string>();
        foreach (var m in config.Modules) ci.TryAdd(ToolImages.ContainerName(m), ToolImages.EcosystemImage(m));
        void Tool(Dictionary<string, string> into, string name) => into.TryAdd(name, Image(name));
        if (s.SecretScan) Tool(ci, "gitleaks");
        if (UsesTrivy) Tool(ci, "trivy");
        if (s.DependencyScanners.Contains(DependencyScanner.OwaspDependencyCheck)) Tool(ci, "dependency-check");
        if (s.DependencyScanners.Contains(DependencyScanner.OsvScanner)) Tool(ci, "osv-scanner");
        if (s.DependencyScanners.Contains(DependencyScanner.Snyk)) Tool(ci, "snyk");
        if (config.Quality.Enabled && SonarMode == SonarMode.Cli) Tool(ci, "sonar-scanner");
        if (ImageModules.Count > 0 && IsKubernetes) { Tool(ci, "kaniko"); Tool(ci, "crane"); }
        if (config.AiReview.Enabled) Tool(ci, "python");
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
        if (HasDast) Tool(deploy, "zap");
        DeployContainers = deploy;
    }

    public string Image(string tool) =>
        Config.Agent.ImageOverrides.TryGetValue(tool, out var o) ? o : ToolImages.Tools[tool];

    public IEnumerable<KeyValuePair<string, string>> AllContainers =>
        CiContainers.Concat(DeployContainers.Where(d => !CiContainers.ContainsKey(d.Key)));

    /// <summary>Declarative <c>when</c> condition for one branch glob.</summary>
    public static string BranchCondition(string pattern) =>
        $"branch pattern: {Quote(pattern)}, comparator: 'GLOB'";

    /// <summary>Condition matching any branch that deploys somewhere.</summary>
    public string DeployBranchesCondition()
    {
        var patterns = DeployEnvironments.Select(e => e.BranchPattern).Distinct().ToList();
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
