using PipelineForge.Core.Model;

namespace PipelineForge.Core.Compliance;

public sealed record ComplianceControl(string Id, string Title, string Implementation);

public sealed record ComplianceResult(IReadOnlyList<string> Changes, IReadOnlyList<string> Warnings);

/// <summary>
/// Applies the minimum controls of a compliance profile to a config. The changes are
/// reported back so the UI can show what was switched on and why.
/// </summary>
public static class ComplianceEnforcer
{
    public static readonly IReadOnlyList<ComplianceControl> Iso27001Controls =
    [
        new("A.5.37", "Documented operating procedures", "docs/PIPELINE.md describes every stage, credential and plugin."),
        new("A.8.8", "Management of technical vulnerabilities", "Dependency scanning, container image scanning and a CycloneDX SBOM on every build."),
        new("A.8.9", "Configuration management", "Infrastructure-as-code scanning; environments are versioned in jenkins/config/environments.yaml."),
        new("A.8.15", "Logging", "All scan reports are archived with fingerprints and published as SARIF."),
        new("A.8.25", "Secure development life cycle", "Quality gate enforced before anything is built into an image."),
        new("A.8.28", "Secure coding", "Static analysis (SonarQube/SonarCloud) and secret scanning on every branch and pull request."),
        new("A.8.29", "Security testing in development and acceptance", "DAST (OWASP ZAP) against test/acceptance environments after deployment."),
        new("A.8.31", "Separation of development, test and production environments", "Separate DTAP environments, credentials and branch rules."),
        new("A.8.32", "Change management", "Production deploys only from the release branch, after a recorded manual approval by an authorised group."),
    ];

    public static ComplianceResult Apply(PipelineConfig c)
    {
        var changes = new List<string>();
        var warnings = new List<string>();
        if (c.Compliance.Profile != ComplianceProfile.Iso27001) return new(changes, warnings);

        void Set(bool current, Action apply, string message)
        {
            if (current) return;
            apply();
            changes.Add(message);
        }

        var s = c.Security;
        Set(s.SecretScan, () => s.SecretScan = true, "A.8.28: secret scanning enabled.");
        Set(s.DependencyScanners.Count > 0, () => s.DependencyScanners.Add(DependencyScanner.TrivyFs), "A.8.8: Trivy dependency scanning enabled.");
        Set(s.Sbom, () => s.Sbom = true, "A.8.8: SBOM generation enabled.");
        if (c.Modules.Any(m => m.Dockerfile is not null))
            Set(s.ContainerScan, () => s.ContainerScan = true, "A.8.8: container image scanning enabled.");
        Set(s.IacScan, () => s.IacScan = true, "A.8.9: infrastructure-as-code scanning enabled.");
        Set(s.FailOnSeverity <= Severity.High, () => s.FailOnSeverity = Severity.High, "A.8.8: builds now fail on high and critical findings.");

        var q = c.Quality;
        Set(q.Enabled, () => q.Enabled = true, "A.8.28: static analysis (Sonar) enabled.");
        Set(q.WaitForQualityGate && q.AbortOnQualityGateFailure,
            () => { q.WaitForQualityGate = true; q.AbortOnQualityGateFailure = true; },
            "A.8.25: the quality gate now blocks the pipeline.");

        Set(c.Options.DisableConcurrentBuilds, () => c.Options.DisableConcurrentBuilds = true, "A.8.32: concurrent builds disabled.");
        Set(c.Options.BuildsToKeep >= 30, () => c.Options.BuildsToKeep = 30, "A.8.15: build history kept for at least 30 builds.");

        var deploying = c.Environments.Where(e => e.Deploy.Type != DeployTargetType.None).ToList();
        var production = deploying.Where(IsProduction).ToList();
        if (production.Count == 0 && deploying.Count > 0) production.Add(deploying[^1]);
        foreach (var prod in production)
            Set(prod.RequiresApproval, () => prod.RequiresApproval = true, $"A.8.32: manual approval required before deploying to {prod.DisplayName}.");

        if (!s.Dast.Enabled)
        {
            var targets = deploying.Where(e => !production.Contains(e) && e.Url is not null).Select(e => e.Key).ToList();
            if (targets.Count > 0)
            {
                s.Dast.Enabled = true;
                s.Dast.Environments = targets;
                changes.Add($"A.8.29: DAST enabled for {string.Join(", ", targets)}.");
            }
            else
            {
                warnings.Add("A.8.29: no test or acceptance environment with a URL, so DAST cannot run. Add a URL to enable it.");
            }
        }
        if (deploying.Count == 0)
            warnings.Add("A.8.31: no deploy environments configured; DTAP separation cannot be shown by this pipeline.");

        return new(changes, warnings);
    }

    public static bool IsProduction(EnvironmentConfig e) =>
        e.Key is "prod" or "production" or "prd" or "p"
        || e.BranchPattern is "main" or "master";
}
