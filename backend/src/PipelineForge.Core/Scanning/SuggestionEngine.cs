using System.Text.RegularExpressions;
using PipelineForge.Core.Model;

namespace PipelineForge.Core.Scanning;

public sealed record Suggestion(PipelineConfig Config, IReadOnlyList<string> Reasons);

/// <summary>Turns a <see cref="RepoProfile"/> into a sensible starting config, explaining each choice.</summary>
public static partial class SuggestionEngine
{
    [GeneratedRegex(@"[^a-z0-9._-]+")] private static partial Regex NameCleanRx();
    [GeneratedRegex(@"^[A-Za-z0-9_.:-]{1,400}$")] private static partial Regex SonarKeyRx();
    [GeneratedRegex(@"^[a-z0-9_-]{1,255}$")] private static partial Regex SonarOrgRx();

    public static Suggestion Suggest(RepoProfile profile, string repoName, string? defaultBranch = null)
    {
        var reasons = new List<string>();
        var name = CleanName(repoName);
        var config = new PipelineConfig { Name = name };

        config.Modules = profile.Modules.Select(m => new ModuleConfig
        {
            Name = m.Name,
            Path = m.Path,
            Ecosystem = m.Ecosystem,
            LanguageVersion = m.LanguageVersion,
            Dockerfile = m.Dockerfile,
            ImageName = m.Dockerfile is null ? null : $"{name}/{m.Name}",
        }).ToList();
        foreach (var m in profile.Modules)
            reasons.Add($"Module '{m.Name}' ({m.Ecosystem}{(m.LanguageVersion is null ? "" : " " + m.LanguageVersion)}) at '{m.Path}', from {string.Join(", ", m.Evidence)}.");
        if (config.Modules.Count == 0)
        {
            config.Modules.Add(new ModuleConfig { Name = name, Path = ".", Ecosystem = Ecosystem.Maven });
            reasons.Add("No build files detected: added a placeholder Maven module. Change it to match the project.");
        }

        var hasImages = config.Modules.Any(m => m.Dockerfile is not null);
        if (hasImages)
        {
            config.Registry = new RegistryConfig { Host = "registry.example.com" };
            reasons.Add("Dockerfile found: images are built, scanned and pushed. Set the registry host.");
        }

        config.Quality.ProjectKey = profile.SonarProjectKey is { } key && SonarKeyRx().IsMatch(key) ? key : name;
        if (profile.SonarOrganization is { } org && SonarOrgRx().IsMatch(org))
        {
            config.Quality.Platform = QualityPlatform.SonarCloud;
            config.Quality.Organization = org;
            reasons.Add("sonar-project.properties has an organisation: using SonarCloud.");
        }

        var scanners = new List<DependencyScanner> { DependencyScanner.TrivyFs };
        if (config.Modules.Any(m => m.Ecosystem is Ecosystem.Maven or Ecosystem.Gradle))
        {
            scanners.Add(DependencyScanner.OwaspDependencyCheck);
            reasons.Add("JVM project: OWASP Dependency-Check added next to Trivy.");
        }
        if (config.Modules.Any(m => m.Ecosystem is Ecosystem.Npm or Ecosystem.Pnpm or Ecosystem.Yarn))
        {
            scanners.Add(DependencyScanner.NpmAudit);
            reasons.Add("Node project: npm audit added.");
        }
        config.Security.DependencyScanners = scanners;
        config.Security.ContainerScan = hasImages;
        config.Security.IacScan = hasImages || profile.HelmCharts.Count > 0 || profile.HasKubernetesManifests || profile.HasTerraform;
        if (config.Security.IacScan) reasons.Add("Infrastructure code found (Dockerfile, Helm, Kubernetes or Terraform): IaC scan enabled.");

        var deployType = DeployTargetType.None;
        var chartPath = "deploy/helm/chart";
        if (hasImages && (profile.HelmCharts.Count > 0 || profile.HasKubernetesManifests))
        {
            deployType = DeployTargetType.Helm;
            if (profile.HelmCharts.FirstOrDefault() is { } chart && chart != ".") chartPath = chart;
            reasons.Add(profile.HelmCharts.Count > 0
                ? $"Helm chart at '{chartPath}': deploying with Helm."
                : "Kubernetes manifests found: deploying with Helm (a minimal chart is generated).");
        }
        else if (hasImages && profile.HasDockerCompose)
        {
            deployType = DeployTargetType.DockerSsh;
            reasons.Add("Compose file found: deploying with Docker Compose over SSH.");
        }
        else if (hasImages)
        {
            deployType = DeployTargetType.Helm;
            reasons.Add("Container image without deploy hints: Helm suggested. Change the target per environment.");
        }

        var prodBranch = defaultBranch is "master" ? "master" : "main";
        config.Environments =
        [
            Environment("dev", "Development", "develop", deployType, chartPath, name, false),
            Environment("test", "Test", "develop", deployType, chartPath, name, false),
            Environment("acc", "Acceptance", "release/*", deployType, chartPath, name, false),
            Environment("prod", "Production", prodBranch, deployType, chartPath, name, true),
        ];
        reasons.Add($"DTAP: develop → Development and Test, release/* → Acceptance, {prodBranch} → Production with approval.");
        if (deployType == DeployTargetType.None)
            reasons.Add("No container image: environments are listed without a deploy target. Pick one per environment.");

        if (profile.ExistingJenkinsfile is not null)
            reasons.Add($"Existing {profile.ExistingJenkinsfile} found: import it to keep custom stages.");
        reasons.AddRange(profile.Notes);
        return new Suggestion(config, reasons);
    }

    private static EnvironmentConfig Environment(string key, string display, string branch, DeployTargetType type, string chart, string app, bool approval) => new()
    {
        Key = key,
        DisplayName = display,
        BranchPattern = branch,
        RequiresApproval = approval,
        Deploy = new DeployTarget
        {
            Type = type,
            ChartPath = chart,
            Namespace = $"{app}-{key}".Length <= 63 ? $"{app}-{key}".Replace('.', '-').Replace('_', '-') : key,
            ReleaseName = app.Replace('.', '-').Replace('_', '-'),
            RemoteDir = $"/opt/{app}",
            AppName = $"{app}-{key}".Replace('.', '-').Replace('_', '-'),
        },
    };

    public static string CleanName(string repoName)
    {
        var last = repoName.Contains('/') ? repoName[(repoName.LastIndexOf('/') + 1)..] : repoName;
        var n = NameCleanRx().Replace(last.ToLowerInvariant(), "-").Trim('-', '.', '_');
        if (n.Length == 0) n = "app";
        return n.Length > 63 ? n[..63] : n;
    }
}
