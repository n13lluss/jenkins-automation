using PipelineForge.Core.Generation;
using PipelineForge.Core.Model;

namespace PipelineForge.Tests.Generation;

/// <summary>Build once and promote, attestations, Dockerfile checks and the Kyverno cluster policy.</summary>
public class SupplyChainTests
{
    public static PipelineConfig Promoting(AgentType agent = AgentType.Kubernetes)
    {
        var c = WorkflowCoverageTests.SecureJava(agent);
        c.Environments.Single(e => e.Key == "acc").PromoteFrom = "test";
        c.Environments.Single(e => e.Key == "prod").PromoteFrom = "acc";
        c.Signing.Attestations = true;
        c.ClusterPolicy.Enabled = true;
        c.Security.DockerfileLint = true;
        c.Security.AllowedBaseRegistries = ["docker.io", "mcr.microsoft.com"];
        return c;
    }

    private static Dictionary<string, string> Files(PipelineConfig config)
    {
        var result = PipelineGenerator.Generate(config);
        Assert.True(result.Success, string.Join("\n", result.Errors.Select(e => $"{e.Path}: {e.Message}")));
        return result.Files.ToDictionary(f => f.Path, f => f.Content);
    }

    [Theory]
    [InlineData(AgentType.Kubernetes)]
    [InlineData(AgentType.Docker)]
    public void Promoted_environments_deploy_the_digest_of_the_previous_environment(AgentType agent)
    {
        var f = Files(Promoting(agent));
        var deploy = f["jenkins/stages/deploy.groovy"];
        var jf = f["Jenkinsfile"];

        Assert.Contains("crane digest \"$REGISTRY/shop/orders-api:pf-env-$PROMOTE_FROM\"", deploy);
        Assert.Contains("crane tag \"$REGISTRY/shop/orders-api@$IMAGE_DIGEST_API\" \"pf-env-$DEPLOY_ENV\"", deploy);
        Assert.Contains("resolvePromoted(common, cfg)", deploy);
        // Only environments that others promote from get a tag.
        Assert.Contains("cfg.key in ['acc', 'test']", deploy);
        Assert.Contains("promoteFrom: \"acc\"", f["jenkins/config/environments.yaml"]);
        Assert.Contains("promoteFrom: \"test\"", f["jenkins/config/environments.yaml"]);

        // The image is built on the develop branch only; main and release branches reuse it.
        var build = jf[jf.IndexOf("stage('Build image')", StringComparison.Ordinal)..];
        build = build[..build.IndexOf("stage(", 10, StringComparison.Ordinal)];
        Assert.Contains("develop", build);
        Assert.DoesNotContain("main", build);
        Assert.DoesNotContain("release", build);
    }

    [Fact]
    public void Every_deploy_uses_a_digest()
    {
        var c = Samples.MavenHelm();
        var f = Files(c);
        Assert.Contains("images.api.digest=\"$IMAGE_DIGEST_API\"", f["jenkins/stages/deploy.groovy"]);

        var compose = Samples.MavenHelm();
        foreach (var e in compose.Environments) e.Deploy.Type = DeployTargetType.DockerSsh;
        Assert.Contains("@${IMAGE_DIGEST_API:?", Files(compose)["deploy/docker/compose.dev.yml"]);
    }

    [Fact]
    public void Attestations_are_signed_and_verified()
    {
        var f = Files(Promoting());
        var deploy = f["jenkins/stages/deploy.groovy"];
        Assert.Contains("--type cyclonedx --predicate reports/attestations/sbom-api.cdx.json", deploy);
        Assert.Contains("--type slsaprovenance1 --predicate reports/attestations/provenance-api.json", deploy);
        Assert.Contains("cosign verify-attestation", deploy);
        Assert.Contains("jenkins/scripts/provenance.py", f.Keys);
        Assert.Contains("pipelineforge.dev/jenkins-pipeline/v1", f["jenkins/scripts/provenance.py"]);
    }

    [Fact]
    public void Dockerfile_checks_use_hadolint_and_require_a_user()
    {
        var f = Files(Promoting());
        Assert.Contains("stage('Dockerfile lint')", f["Jenkinsfile"]);
        Assert.Contains("hadolint --config security/.hadolint.yaml", f["jenkins/stages/security.groovy"]);
        Assert.Contains("no USER instruction", f["jenkins/stages/security.groovy"]);
        var config = f["security/.hadolint.yaml"];
        Assert.Contains("failure-threshold: error", config);
        Assert.Contains("DL3007", config);
        Assert.Contains("- \"mcr.microsoft.com\"", config);
    }

    [Fact]
    public void Cluster_policy_requires_signed_images_and_attestations()
    {
        var policy = Files(Promoting())["deploy/policies/kyverno-policies.yaml"];
        Assert.Contains("kind: ClusterPolicy", policy);
        Assert.Contains("validationFailureAction: Enforce", policy);
        Assert.Contains("registry.example.com/shop/orders-api*", policy);
        Assert.Contains("pipelineforge-cosign-pub", policy);
        Assert.Contains("https://cyclonedx.org/bom", policy);
        Assert.Contains("disallow-latest-tag", policy);
        Assert.Contains("require-run-as-non-root", policy);
    }

    [Fact]
    public void Features_stay_off_by_default()
    {
        var f = Files(Samples.MavenHelm());
        Assert.DoesNotContain("security/.hadolint.yaml", f.Keys);
        Assert.DoesNotContain("deploy/policies/kyverno-policies.yaml", f.Keys);
        Assert.DoesNotContain("jenkins/scripts/provenance.py", f.Keys);
        Assert.DoesNotContain("crane tag", f["jenkins/stages/deploy.groovy"]);
    }

    [Fact]
    public void Generated_groovy_stays_balanced()
    {
        foreach (var agent in new[] { AgentType.Kubernetes, AgentType.Docker })
            foreach (var (path, content) in Files(Promoting(agent)).Where(kv => kv.Key.StartsWith("Jenkinsfile") || kv.Key.EndsWith(".groovy")))
                Assert.True(PipelineForge.Core.Validation.ConfigValidator.BracesBalanced(content), $"{agent} {path}");
    }

    [Theory]
    [InlineData("prod", "test")]   // later environment
    [InlineData("nope", "test")]   // unknown
    [InlineData("acc", "acc")]     // itself
    [InlineData("acc'; rm -rf /", "acc")]
    public void Promotion_must_come_from_an_earlier_environment(string from, string env)
    {
        var c = Samples.MavenHelm();
        var i = c.Environments.FindIndex(e => e.Key == env);
        c.Environments[i].PromoteFrom = from;
        Assert.Contains(PipelineGenerator.Generate(c).Errors, e => e.Path == $"environments[{i}].promoteFrom");
    }

    [Fact]
    public void Dependent_options_are_validated()
    {
        var c = Samples.MavenHelm();
        c.Signing.Enabled = false;
        c.Signing.Attestations = true;
        c.ClusterPolicy.Enabled = true;
        c.Security.DockerfileLint = true;
        c.Security.AllowedBaseRegistries = ["docker.io; curl evil"];
        var errors = PipelineGenerator.Generate(c).Errors.Select(e => e.Path).ToList();
        Assert.Contains("signing.attestations", errors);
        Assert.Contains("clusterPolicy", errors);
        Assert.Contains("security.allowedBaseRegistries", errors);
    }
}
