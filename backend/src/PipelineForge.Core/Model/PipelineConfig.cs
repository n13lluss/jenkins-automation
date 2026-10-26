namespace PipelineForge.Core.Model;

/// <summary>
/// The single source of truth for a generated pipeline. Scans, blank projects,
/// imported Jenkinsfiles and presets all produce or modify one of these.
/// </summary>
public sealed class PipelineConfig
{
    public string Name { get; set; } = "app";
    /// <summary>InRepo keeps the pipeline in the application repository; SharedLibrary moves it to a protected library.</summary>
    public PipelineLayout Layout { get; set; } = PipelineLayout.InRepo;
    public SharedLibraryConfig Library { get; set; } = new();
    public AgentConfig Agent { get; set; } = new();
    public List<ModuleConfig> Modules { get; set; } = [];
    public RegistryConfig? Registry { get; set; }
    /// <summary>Resolve all dependencies through a controlled proxy (Nexus, Artifactory) instead of public registries.</summary>
    public DependencyProxyConfig? DependencyProxy { get; set; }
    public QualityConfig Quality { get; set; } = new();
    public SecurityConfig Security { get; set; } = new();
    public SigningConfig Signing { get; set; } = new();
    public ClusterPolicyConfig ClusterPolicy { get; set; } = new();
    public RateLimitCheckConfig RateLimit { get; set; } = new();
    public TicketingConfig Ticketing { get; set; } = new();
    public StageTriggers Triggers { get; set; } = new();
    public ComplianceConfig Compliance { get; set; } = new();
    public List<EnvironmentConfig> Environments { get; set; } = [];
    public AiReviewConfig AiReview { get; set; } = new();
    public List<CustomStage> CustomStages { get; set; } = [];
    public PipelineOptions Options { get; set; } = new();
}

public enum PipelineLayout { InRepo, SharedLibrary }

/// <summary>
/// Jenkins shared library holding the pipeline. The application repository only keeps a thin Jenkinsfile, so
/// branches and pull requests cannot change stage logic, scripts, agent pods or deploy targets.
/// </summary>
public sealed class SharedLibraryConfig
{
    /// <summary>Library name as configured in Jenkins (folder or global library).</summary>
    public string Name { get; set; } = "pipelineforge";
    /// <summary>owner/name of the library repository, for the Jenkins folder library and the library pull request.</summary>
    public string? Repository { get; set; }
    /// <summary>Branch or tag Jenkins loads by default; overrides should be disabled in Jenkins.</summary>
    public string DefaultVersion { get; set; } = "main";
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

public enum SecretScanScope { History, Diff }

public enum ScannerTool { Trivy, Snyk }

public enum ProxyType { Nexus, Artifactory }

/// <summary>
/// Repository manager acting as the single source of dependencies. Paths are relative to
/// <see cref="BaseUrl"/>; leave one empty to keep that ecosystem on its default registry.
/// </summary>
public sealed class DependencyProxyConfig
{
    public ProxyType Type { get; set; } = ProxyType.Nexus;
    public string BaseUrl { get; set; } = "https://nexus.example.com";
    /// <summary>Username with password credential for the repository manager.</summary>
    public string CredentialId { get; set; } = "nexus-credentials";
    public string? MavenPath { get; set; } = "repository/maven-public";
    public string? NpmPath { get; set; } = "repository/npm-proxy";
    public string? PypiPath { get; set; } = "repository/pypi-proxy/simple";
    public string? NugetPath { get; set; } = "repository/nuget-proxy/index.json";
    public string? GoPath { get; set; } = "repository/go-proxy";
    public string? ComposerPath { get; set; } = "repository/packagist-proxy";
}

/// <summary>Cosign key-pair signing: sign the pushed digest, verify it before every deploy.</summary>
public sealed class SigningConfig
{
    public bool Enabled { get; set; }
    /// <summary>Secret file credential holding cosign.key.</summary>
    public string KeyCredentialId { get; set; } = "cosign-key";
    /// <summary>Secret text credential holding the key password.</summary>
    public string PasswordCredentialId { get; set; } = "cosign-password";
    /// <summary>Public key committed to the repository and used for verification.</summary>
    public string PublicKeyPath { get; set; } = "security/cosign.pub";
    /// <summary>Upload signatures to the public Rekor transparency log. Off for private images.</summary>
    public bool TransparencyLog { get; set; }
    /// <summary>Also attach signed attestations: the image SBOM (CycloneDX) and SLSA build provenance.</summary>
    public bool Attestations { get; set; }
}

/// <summary>Admission policies (Kyverno) so the cluster itself refuses unsigned or unsafe images.</summary>
public sealed class ClusterPolicyConfig
{
    public bool Enabled { get; set; }
    public bool DisallowLatestTag { get; set; } = true;
    public bool RequireRunAsNonRoot { get; set; } = true;
}

public enum LoginFormat { Form, Json }

/// <summary>How to log in to the application under test (authenticated DAST, rate-limit check).</summary>
public sealed class LoginConfig
{
    public string LoginPath { get; set; } = "/login";
    public LoginFormat Format { get; set; } = LoginFormat.Form;
    public string UsernameField { get; set; } = "username";
    public string PasswordField { get; set; } = "password";
    public string LogoutPath { get; set; } = "/logout";
}

/// <summary>Start the just-built image on the agent and run a passive ZAP scan against it.</summary>
public sealed class EphemeralDastConfig
{
    public bool Enabled { get; set; }
    /// <summary>Module whose image is scanned; the first image module when empty.</summary>
    public string? Module { get; set; }
    public int ContainerPort { get; set; } = 8080;
    public string HealthPath { get; set; } = "/actuator/health";
    /// <summary>Kubernetes image pull secret for the scan pod (private registries).</summary>
    public string? ImagePullSecret { get; set; }
    /// <summary>KEY=value pairs for the app, e.g. SPRING_PROFILES_ACTIVE=dast. No secrets: these are written into the pipeline.</summary>
    public List<string> AppEnvironment { get; set; } = [];
}

/// <summary>Two-role authenticated scan: ZAP active scan plus an access-control and session check.</summary>
public sealed class AuthenticatedDastConfig
{
    public bool Enabled { get; set; }
    /// <summary>Environment to scan; needs a URL.</summary>
    public string Environment { get; set; } = "test";
    public LoginConfig Login { get; set; } = new();
    /// <summary>Username with password credential of the low-privileged test account.</summary>
    public string UserCredentialId { get; set; } = "zap-user";
    /// <summary>Username with password credential of the admin test account.</summary>
    public string AdminCredentialId { get; set; } = "zap-admin";
    /// <summary>A page any logged-in user may open; used for logout and session checks.</summary>
    public string ProtectedPath { get; set; } = "/";
    /// <summary>Paths only admins may reach; the user role must be refused.</summary>
    public List<string> AdminOnlyPaths { get; set; } = ["/admin"];
    /// <summary>Also run before the production approval (in addition to the schedule).</summary>
    public bool BeforeProduction { get; set; }
}

/// <summary>Scheduled scans of a live environment, independent of code changes (Jenkinsfile.scheduled).</summary>
public sealed class ScheduledScanConfig
{
    public bool Enabled { get; set; }
    public string Environment { get; set; } = "test";
    public string BaselineCron { get; set; } = "H */6 * * *";
    /// <summary>The first scheduled run each day also runs the authenticated scan.</summary>
    public bool AuthenticatedDaily { get; set; } = true;
}

/// <summary>Post-deploy check that the login endpoint throttles or locks out repeated failures.</summary>
public sealed class RateLimitCheckConfig
{
    public bool Enabled { get; set; }
    public LoginConfig Login { get; set; } = new();
    /// <summary>A username that does not exist or is a dedicated probe account, never a real user.</summary>
    public string TestUsername { get; set; } = "pf-ratelimit-probe";
    public int Attempts { get; set; } = 20;
    public List<int> ExpectedStatuses { get; set; } = [429, 423];
    public List<string> Environments { get; set; } = ["test"];
}

public enum TicketProvider { None, Jira }

public enum TicketSource { SecretScan, Dependencies, Container, Iac, Sonar, Dast, AccessControl, AiReview, Dockerfile }

/// <summary>Turn findings into deduplicated tickets. Snyk can also create Jira tickets natively.</summary>
public sealed class TicketingConfig
{
    public TicketProvider Provider { get; set; } = TicketProvider.None;
    /// <summary>Site name configured in the Jira Steps plugin.</summary>
    public string JiraSite { get; set; } = "jira";
    public string ProjectKey { get; set; } = "SEC";
    public string IssueType { get; set; } = "Bug";
    public Severity MinSeverity { get; set; } = Severity.High;
    public int MaxPerBuild { get; set; } = 10;
    /// <summary>Also file tickets from pull request builds (otherwise deploy branches and scheduled runs only).</summary>
    public bool PullRequests { get; set; }
    public List<TicketSource> Sources { get; set; } =
        [TicketSource.SecretScan, TicketSource.Dependencies, TicketSource.Container, TicketSource.Iac, TicketSource.Sonar, TicketSource.Dast, TicketSource.AccessControl];
}

public enum StageTrigger { Always, PullRequests, DeployBranches }

/// <summary>When groups of CI stages run. Build and tests always run.</summary>
public sealed class StageTriggers
{
    /// <summary>Static analysis, dependency, secret, license and IaC scans.</summary>
    public StageTrigger Checks { get; set; } = StageTrigger.Always;
    /// <summary>Image build, image scan and ephemeral DAST.</summary>
    public StageTrigger Package { get; set; } = StageTrigger.Always;
}

public sealed class SecurityConfig
{
    public bool SecretScan { get; set; } = true;
    public SecretScanScope SecretScanScope { get; set; } = SecretScanScope.History;
    /// <summary>Fail on floating or unpinned dependency versions (Maven Enforcer).</summary>
    public bool DependencyPinning { get; set; }
    public List<DependencyScanner> DependencyScanners { get; set; } = [DependencyScanner.TrivyFs];
    public bool Sbom { get; set; } = true;
    public bool LicenseCheck { get; set; }
    public bool ContainerScan { get; set; } = true;
    public ScannerTool ContainerScanner { get; set; } = ScannerTool.Trivy;
    public bool IacScan { get; set; }
    public ScannerTool IacScanner { get; set; } = ScannerTool.Trivy;
    /// <summary>Skip the IaC scan when no infrastructure files changed.</summary>
    public bool IacOnlyWhenChanged { get; set; }
    /// <summary>Register deployed dependencies and images with Snyk for continuous CVE monitoring.</summary>
    public bool SnykMonitor { get; set; }
    /// <summary>Hadolint plus a USER check for every Dockerfile.</summary>
    public bool DockerfileLint { get; set; }
    /// <summary>Registries base images may come from (Hadolint trustedRegistries); empty allows any.</summary>
    public List<string> AllowedBaseRegistries { get; set; } = [];
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
    public EphemeralDastConfig Ephemeral { get; set; } = new();
    public AuthenticatedDastConfig Authenticated { get; set; } = new();
    public ScheduledScanConfig Schedule { get; set; } = new();
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
    /// <summary>
    /// Build once, promote: deploy the image that is running in this earlier environment instead of
    /// building a new one. Null means this environment's branch builds the image.
    /// </summary>
    public string? PromoteFrom { get; set; }
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
