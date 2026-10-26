using PipelineForge.Core.Model;

namespace PipelineForge.Core.Generation;

/// <summary>
/// Pinned container images for every tool the generated pipeline uses. Review and bump
/// these regularly; per-pipeline overrides live in <see cref="AgentConfig.ImageOverrides"/>.
/// </summary>
public static class ToolImages
{
    public static readonly IReadOnlyDictionary<string, string> Tools = new Dictionary<string, string>
    {
        ["gitleaks"] = "zricethezav/gitleaks:v8.28.0",
        ["trivy"] = "aquasec/trivy:0.67.2",
        ["dependency-check"] = "owasp/dependency-check:12.1.8",
        ["osv-scanner"] = "ghcr.io/google/osv-scanner:v2.2.4",
        ["snyk"] = "snyk/snyk:linux",
        ["snyk-maven"] = "snyk/snyk:maven-3-jdk-21",
        ["cosign"] = "gcr.io/projectsigstore/cosign:v2.6.1-dev",
        ["sonar-scanner"] = "sonarsource/sonar-scanner-cli:11.5",
        ["zap"] = "ghcr.io/zaproxy/zaproxy:2.16.1",
        ["kaniko"] = "gcr.io/kaniko-project/executor:v1.24.0-debug",
        ["crane"] = "gcr.io/go-containerregistry/crane:debug",
        ["helm"] = "alpine/k8s:1.33.4",
        ["azure-cli"] = "mcr.microsoft.com/azure-cli:2.77.0",
        ["ssh"] = "alpine:3.22",
        ["curl"] = "curlimages/curl:8.16.0",
        ["python"] = "python:3.13-slim",
    };

    public static string DefaultVersion(Ecosystem e) => e switch
    {
        Ecosystem.Maven or Ecosystem.Gradle => "21",
        Ecosystem.Npm or Ecosystem.Pnpm or Ecosystem.Yarn => "22",
        Ecosystem.DotNet => "10.0",
        Ecosystem.Python => "3.13",
        Ecosystem.Go => "1.25",
        Ecosystem.Composer => "8.3",
        _ => "latest",
    };

    /// <summary>Container name used in pod templates and <c>common.inTool()</c>.</summary>
    public static string ContainerName(ModuleConfig m)
    {
        var v = (m.LanguageVersion ?? DefaultVersion(m.Ecosystem)).ToLowerInvariant().Replace('.', '-').Replace('_', '-');
        var name = $"{EcosystemTool(m.Ecosystem)}-{v}";
        return name.Length > 63 ? name[..63].TrimEnd('-') : name;
    }

    public static string EcosystemTool(Ecosystem e) => e switch
    {
        Ecosystem.Maven => "maven",
        Ecosystem.Gradle => "gradle",
        Ecosystem.Npm or Ecosystem.Pnpm or Ecosystem.Yarn => "node",
        Ecosystem.DotNet => "dotnet",
        Ecosystem.Python => "python",
        Ecosystem.Go => "go",
        Ecosystem.Composer => "composer",
        _ => throw new ArgumentOutOfRangeException(nameof(e)),
    };

    public static string EcosystemImage(ModuleConfig m)
    {
        var v = m.LanguageVersion ?? DefaultVersion(m.Ecosystem);
        return m.Ecosystem switch
        {
            Ecosystem.Maven => $"maven:3.9.11-eclipse-temurin-{v}",
            Ecosystem.Gradle => $"eclipse-temurin:{v}-jdk",
            Ecosystem.Npm or Ecosystem.Pnpm or Ecosystem.Yarn => $"node:{v}-bookworm-slim",
            Ecosystem.DotNet => $"mcr.microsoft.com/dotnet/sdk:{v}",
            Ecosystem.Python => $"python:{v}-slim",
            Ecosystem.Go => $"golang:{v}",
            Ecosystem.Composer => $"composer:2.8",
            _ => throw new ArgumentOutOfRangeException(nameof(m)),
        };
    }
}
