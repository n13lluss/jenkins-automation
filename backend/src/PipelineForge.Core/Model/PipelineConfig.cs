namespace PipelineForge.Core.Model;

/// <summary>
/// The single source of truth for a generated pipeline. Scans, blank projects,
/// imported Jenkinsfiles and presets all produce or modify one of these.
/// </summary>
public sealed class PipelineConfig
{
    public string Name { get; set; } = "app";
    public AgentConfig Agent { get; set; } = new();
    public List<ModuleConfig> Modules { get; set; } = [];
    public RegistryConfig? Registry { get; set; }
    public QualityConfig Quality { get; set; } = new();
    public SecurityConfig Security { get; set; } = new();
    public ComplianceConfig Compliance { get; set; } = new();
    public List<EnvironmentConfig> Environments { get; set; } = [];
    public AiReviewConfig AiReview { get; set; } = new();
    public List<CustomStage> CustomStages { get; set; } = [];
    public PipelineOptions Options { get; set; } = new();
}

public enum AgentType { Kubernetes, Docker }

public sealed class AgentConfig
{
    public AgentType Type { get; set; } = AgentType.Kubernetes;
    /// <summary>Node label used for Docker agents (the node needs a Docker daemon).</summary>
    public string DockerLabel { get; set; } = "docker";
    /// <summary>Optional Kubernetes cloud name configured in Jenkins.</summary>
    public string? KubernetesCloud { get; set; }
    /// <summary>Overrides for tool images, keyed by tool name (see <c>ToolImages</c>).</summary>
    public Dictionary<string, string> ImageOverrides { get; set; } = [];
}

public enum Ecosystem { Maven, Gradle, Npm, Pnpm, Yarn, DotNet, Python, Go, Composer }

public sealed class ModuleConfig
{
    public string Name { get; set; } = "app";
    /// <summary>Path relative to the repository root, "." for the root.</summary>
    public string Path { get; set; } = ".";
    public Ecosystem Ecosystem { get; set; }
    /// <summary>Java release, Node major, .NET version, Python version, Go version or PHP version.</summary>
    public string? LanguageVersion { get; set; }
    public bool RunTests { get; set; } = true;
    public bool Coverage { get; set; } = true;
    /// <summary>Dockerfile path relative to the repository root; null when the module ships no image.</summary>
    public string? Dockerfile { get; set; }
    /// <summary>Image repository name, without registry and tag.</summary>
    public string? ImageName { get; set; }
}

public sealed class RegistryConfig
{
    /// <summary>Registry host, e.g. registry.example.com or myacr.azurecr.io.</summary>
    public string Host { get; set; } = "";
    public string CredentialId { get; set; } = "registry-credentials";
}

public enum QualityPlatform { SonarQube, SonarCloud }

public sealed class QualityConfig
{
    public bool Enabled { get; set; } = true;
    public QualityPlatform Platform { get; set; } = QualityPlatform.SonarQube;
    /// <summary>Name of the SonarQube installation in Manage Jenkins → System.</summary>
    public string ServerName { get; set; } = "sonarqube";
    public string ProjectKey { get; set; } = "app";
    /// <summary>SonarCloud organisation key; required for SonarCloud.</summary>
    public string? Organization { get; set; }
    public bool WaitForQualityGate { get; set; } = true;
    public bool AbortOnQualityGateFailure { get; set; } = true;
}

public enum DependencyScanner { OwaspDependencyCheck, TrivyFs, OsvScanner, Snyk, NpmAudit }

public enum Severity { Low, Medium, High, Critical }

public enum DastMode { Baseline, Full, Api }

public sealed class SecurityConfig
{
    public bool SecretScan { get; set; } = true;
    public List<DependencyScanner> DependencyScanners { get; set; } = [DependencyScanner.TrivyFs];
    public bool Sbom { get; set; } = true;
    public bool LicenseCheck { get; set; }
    public bool ContainerScan { get; set; } = true;
    public bool IacScan { get; set; }
    public Severity FailOnSeverity { get; set; } = Severity.High;
    public string NvdApiKeyCredentialId { get; set; } = "nvd-api-key";
    public string SnykCredentialId { get; set; } = "snyk-token";
    public DastConfig Dast { get; set; } = new();
}

public sealed class DastConfig
{
    public bool Enabled { get; set; }
    public DastMode Mode { get; set; } = DastMode.Baseline;
    /// <summary>OpenAPI spec URL or path, required for <see cref="DastMode.Api"/>.</summary>
    public string? ApiSpec { get; set; }
    /// <summary>Environment keys to scan after deploying, e.g. ["test", "acc"].</summary>
    public List<string> Environments { get; set; } = ["test"];
}

public enum ComplianceProfile { None, Iso27001 }

public sealed class ComplianceConfig
{
    public ComplianceProfile Profile { get; set; } = ComplianceProfile.None;
    /// <summary>Jenkins user or group allowed to approve production deploys.</summary>
    public string ApproverGroup { get; set; } = "release-managers";
}

public enum DeployTargetType { None, Helm, DockerSsh, AzureAppService }

public sealed class EnvironmentConfig
{
    /// <summary>Short key used in file names and stage names: dev, test, acc, prod.</summary>
    public string Key { get; set; } = "dev";
    public string DisplayName { get; set; } = "Development";
    /// <summary>Glob matched against the branch name, e.g. develop, release/*, main.</summary>
    public string BranchPattern { get; set; } = "develop";
    public string? Url { get; set; }
    public bool RequiresApproval { get; set; }
    public DeployTarget Deploy { get; set; } = new();
}

public sealed class DeployTarget
{
    public DeployTargetType Type { get; set; } = DeployTargetType.None;

    // Helm / Kubernetes
    public string KubeconfigCredentialId { get; set; } = "kubeconfig";
    public string Namespace { get; set; } = "default";
    public string ReleaseName { get; set; } = "app";
    public string ChartPath { get; set; } = "deploy/helm/chart";

    // Docker over SSH
    public string SshCredentialId { get; set; } = "deploy-ssh";
    /// <summary>Secret file credential holding the pinned known_hosts entry (no trust-on-first-use).</summary>
    public string KnownHostsCredentialId { get; set; } = "deploy-known-hosts";
    public string Host { get; set; } = "";
    public string User { get; set; } = "deploy";
    public string RemoteDir { get; set; } = "/opt/app";

    // Azure App Service
    public string AzureCredentialId { get; set; } = "azure-sp";
    public string ResourceGroup { get; set; } = "";
    public string AppName { get; set; } = "";
    public string? Slot { get; set; }
}

public enum AiProvider { Anthropic, OpenAiCompatible, AzureOpenAi }

public sealed class AiReviewConfig
{
    public bool Enabled { get; set; }
    public AiProvider Provider { get; set; } = AiProvider.Anthropic;
    public string Model { get; set; } = "claude-opus-5-5";
    /// <summary>API base URL; empty means the provider default.</summary>
    public string? BaseUrl { get; set; }
    public string CredentialId { get; set; } = "ai-api-key";
    public bool FailOnFindings { get; set; }
    public Severity MinSeverity { get; set; } = Severity.High;
    /// <summary>Only review pull requests (diff against the target branch).</summary>
    public bool PullRequestsOnly { get; set; } = true;
}

public enum CustomStageOrigin { User, Imported }

public sealed class CustomStage
{
    public string Name { get; set; } = "Custom";
    /// <summary>Raw declarative steps. Inserted verbatim, so the UI and docs flag it for review.</summary>
    public string Steps { get; set; } = "";
    public CustomStageOrigin Origin { get; set; } = CustomStageOrigin.User;
}

public sealed class PipelineOptions
{
    public int TimeoutMinutes { get; set; } = 60;
    public int BuildsToKeep { get; set; } = 20;
    public bool DisableConcurrentBuilds { get; set; } = true;
}
