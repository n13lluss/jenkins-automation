using PipelineForge.Core.Compliance;
using PipelineForge.Core.Generation;
using PipelineForge.Core.Model;

namespace PipelineForge.Tests.Generation;

public class ComplianceTests
{
    [Fact]
    public void Iso27001_turns_on_the_minimum_controls()
    {
        var config = Samples.MavenHelm();
        config.Compliance.Profile = ComplianceProfile.Iso27001;
        config.Security = new SecurityConfig { SecretScan = false, DependencyScanners = [], Sbom = false, ContainerScan = false };
        config.Quality.WaitForQualityGate = false;
        config.Environments[3].RequiresApproval = false;

        var result = ComplianceEnforcer.Apply(config);

        Assert.True(config.Security.SecretScan);
        Assert.NotEmpty(config.Security.DependencyScanners);
        Assert.True(config.Security.Sbom && config.Security.ContainerScan && config.Security.IacScan);
        Assert.True(config.Quality.WaitForQualityGate && config.Quality.AbortOnQualityGateFailure);
        Assert.True(config.Environments.Single(e => e.Key == "prod").RequiresApproval);
        Assert.True(config.Security.Dast.Enabled);
        Assert.DoesNotContain("prod", config.Security.Dast.Environments);
        Assert.Contains(result.Changes, c => c.StartsWith("A.8.32"));
    }

    [Fact]
    public void Iso27001_generates_the_evidence_document()
    {
        var result = PipelineGenerator.Generate(Samples.Monorepo());
        var doc = result.Files.Single(f => f.Path == "docs/iso27001-evidence.md").Content;
        Assert.Contains("A.8.8", doc);
        Assert.Contains("A.8.32", doc);
    }

    [Fact]
    public void No_profile_changes_nothing()
    {
        var config = Samples.MavenHelm();
        config.Security.SecretScan = false;
        var result = ComplianceEnforcer.Apply(config);
        Assert.Empty(result.Changes);
        Assert.False(config.Security.SecretScan);
    }
}
