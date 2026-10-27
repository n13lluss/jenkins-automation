namespace PipelineForge.Core.Presets;

public sealed record BuiltInPreset(string Id, string Name, string Description, string ConfigJson);

/// <summary>Presets shipped with PipelineForge. Applied like workspace presets (JSON Merge Patch).</summary>
public static class BuiltInPresets
{
    /// <summary>
    /// The secure Java / Spring Boot workflow from project_workflow.docx: PR checks (build, test,
    /// Sonar, Snyk SCA, Gitleaks on the diff, Snyk IaC when infra changed), then on merge: package,
    /// image, ephemeral ZAP, Snyk container scan, Cosign sign/verify, deploy, rate-limit check,
    /// Snyk monitor, scheduled ZAP (6-hourly baseline, daily authenticated) and Jira tickets.
    /// </summary>
    public static readonly BuiltInPreset SecureJava = new(
        "builtin-secure-java",
        "Secure Java (Spring Boot) workflow",
        "Protected shared library, Maven Enforcer, Nexus, SonarQube gate, Snyk SCA/container/IaC/monitor, Gitleaks on the diff, Hadolint, Cosign with SBOM and provenance attestations, Kyverno admission policy, exception register, ephemeral, authenticated and scheduled ZAP, login rate-limit check, Jira tickets and failure notifications.",
        """
        {
          "layout": "sharedLibrary",
          "clusterPolicy": { "enabled": true, "disallowLatestTag": true, "requireRunAsNonRoot": true },
          "notifications": { "events": ["failure", "fixed", "approval", "productionDeployed"] },
          "dependencyProxy": { "type": "nexus", "baseUrl": "https://nexus.example.com", "credentialId": "nexus-credentials" },
          "quality": { "enabled": true, "platform": "sonarQube", "waitForQualityGate": true, "abortOnQualityGateFailure": true },
          "security": {
            "secretScan": true,
            "secretScanScope": "diff",
            "dependencyPinning": true,
            "dockerfileLint": true,
            "allowedBaseRegistries": ["docker.io", "mcr.microsoft.com", "gcr.io"],
            "exceptionRegister": true,
            "dependencyScanners": ["snyk"],
            "sbom": true,
            "containerScan": true,
            "containerScanner": "snyk",
            "iacScan": true,
            "iacScanner": "snyk",
            "iacOnlyWhenChanged": true,
            "snykMonitor": true,
            "failOnSeverity": "high",
            "dast": {
              "enabled": false,
              "ephemeral": { "enabled": true, "containerPort": 8080, "healthPath": "/actuator/health" },
              "authenticated": { "enabled": true, "environment": "test", "adminOnlyPaths": ["/admin"] },
              "schedule": { "enabled": true, "environment": "test", "baselineCron": "H */6 * * *", "authenticatedDaily": true }
            }
          },
          "signing": { "enabled": true, "attestations": true },
          "rateLimit": { "enabled": true, "environments": ["test"] },
          "ticketing": { "provider": "jira", "projectKey": "SEC", "minSeverity": "high", "pullRequests": true, "sources": ["secretScan", "sonar", "dast", "accessControl"] },
          "triggers": { "checks": "pullRequests", "package": "deployBranches" }
        }
        """);

    public static readonly IReadOnlyList<BuiltInPreset> All = [SecureJava];

    public static BuiltInPreset? Find(string? id) => All.FirstOrDefault(p => p.Id == id);
}
