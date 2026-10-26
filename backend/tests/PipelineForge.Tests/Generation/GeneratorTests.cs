using PipelineForge.Core.Generation;
using PipelineForge.Core.Model;
using PipelineForge.Core.Validation;

namespace PipelineForge.Tests.Generation;

public class GeneratorTests
{
    public static TheoryData<string> SampleNames => new() { "maven", "mono" };

    private static PipelineConfig Sample(string name) => name == "maven" ? Samples.MavenHelm() : Samples.Monorepo();

    [Theory]
    [MemberData(nameof(SampleNames))]
    public void Samples_generate_without_errors(string name)
    {
        var result = PipelineGenerator.Generate(Sample(name));
        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.Contains(result.Files, f => f.Path == "Jenkinsfile");
        Assert.Contains(result.Files, f => f.Path == "docs/PIPELINE.md");
    }

    [Theory]
    [MemberData(nameof(SampleNames))]
    public void Groovy_files_have_balanced_braces_and_no_interpolating_shell_steps(string name)
    {
        var result = PipelineGenerator.Generate(Sample(name));
        foreach (var f in result.Files.Where(f => f.Path == "Jenkinsfile" || f.Path.EndsWith(".groovy")))
        {
            Assert.True(ConfigValidator.BracesBalanced(f.Content), $"{f.Path} has unbalanced braces");
            Assert.DoesNotMatch(@"(?<![A-Za-z])sh\s*""", f.Content);
            Assert.DoesNotContain("script: \"", f.Content);
            var expected = f.Path switch
            {
                "Jenkinsfile" when result.EffectiveConfig.Layout == PipelineForge.Core.Model.PipelineLayout.SharedLibrary => "()\n",
                "Jenkinsfile" => "}\n",
                _ when f.Path.StartsWith("library/vars/") => "}\n",
                _ => "return this\n",
            };
            Assert.EndsWith(expected, f.Content);
        }
    }

    [Fact]
    public void Kubernetes_agents_get_pod_templates_with_every_tool()
    {
        var result = PipelineGenerator.Generate(Samples.MavenHelm());
        var ci = result.Files.Single(f => f.Path == "jenkins/agents/ci-pod.yaml").Content;
        foreach (var tool in new[] { "maven-21", "gitleaks", "trivy", "dependency-check", "kaniko", "crane", "python" })
            Assert.Contains($"- name: {tool}", ci);
        Assert.Contains("automountServiceAccountToken: false", ci);
        var deploy = result.Files.Single(f => f.Path == "jenkins/agents/deploy-pod.yaml").Content;
        Assert.Contains("- name: helm", deploy);
        Assert.Contains("- name: zap", deploy);
    }

    [Fact]
    public void Docker_agents_get_an_image_map_and_no_pod_templates()
    {
        var result = PipelineGenerator.Generate(Samples.Monorepo());
        Assert.DoesNotContain(result.Files, f => f.Path.Contains("jenkins/agents/"));
        var common = result.Files.Single(f => f.Path.EndsWith("jenkins/stages/common.groovy")).Content;
        Assert.Contains("'gradle-21': 'eclipse-temurin:21-jdk'", common);
        Assert.Contains("docker.image(image).inside", common);
    }

    [Fact]
    public void Production_requires_approval_and_deploys_only_from_main()
    {
        var jf = PipelineGenerator.Generate(Samples.MavenHelm()).Files.Single(f => f.Path == "Jenkinsfile").Content;
        var approve = jf.IndexOf("stage('Approve Production')", StringComparison.Ordinal);
        var deploy = jf.IndexOf("stage('Deploy Production')", StringComparison.Ordinal);
        Assert.True(approve > 0 && deploy > approve);
        Assert.Contains("submitter: 'release-managers'", jf);
        Assert.Contains("branch pattern: 'main', comparator: 'GLOB'", jf[deploy..]);
    }

    [Fact]
    public void Images_are_scanned_before_they_are_pushed()
    {
        var jf = PipelineGenerator.Generate(Samples.MavenHelm()).Files.Single(f => f.Path == "Jenkinsfile").Content;
        Assert.True(jf.IndexOf("stage('Container scan')", StringComparison.Ordinal) < jf.IndexOf("stage('Push image')", StringComparison.Ordinal));
    }

    [Fact]
    public void Single_file_selection_returns_only_that_file()
    {
        var result = PipelineGenerator.Generate(Samples.Monorepo(), ["sonar-project.properties"]);
        var file = Assert.Single(result.Files);
        Assert.Contains("sonar.organization=acme", file.Content);
        Assert.Contains("sonar.java.binaries=backend/build/classes", file.Content);
    }

    [Fact]
    public void Generation_does_not_modify_the_input_config()
    {
        var config = Samples.Monorepo();
        config.Security.SecretScan = false;
        var result = PipelineGenerator.Generate(config);
        Assert.False(config.Security.SecretScan);
        Assert.True(result.EffectiveConfig.Security.SecretScan);
    }

    [Fact]
    public void Ai_review_script_is_included_with_the_redactor()
    {
        var result = PipelineGenerator.Generate(Samples.MavenHelm());
        var py = result.Files.Single(f => f.Path == "jenkins/scripts/ai_review.py").Content;
        Assert.Contains("def redact(text):", py);
        Assert.Contains("claude-opus-5-5", result.Files.Single(f => f.Path == "jenkins/stages/ai-review.groovy").Content);
    }

    [Theory]
    [InlineData(Ecosystem.Maven, "mvn -B -ntp")]
    [InlineData(Ecosystem.Gradle, "./gradlew --no-daemon")]
    [InlineData(Ecosystem.Npm, "npm ci")]
    [InlineData(Ecosystem.Pnpm, "corepack pnpm install --frozen-lockfile")]
    [InlineData(Ecosystem.Yarn, "corepack yarn install --immutable")]
    [InlineData(Ecosystem.DotNet, "dotnet test -c Release")]
    [InlineData(Ecosystem.Python, "pytest --junitxml")]
    [InlineData(Ecosystem.Go, "go test ./...")]
    [InlineData(Ecosystem.Composer, "composer install")]
    public void Every_ecosystem_has_a_build(Ecosystem eco, string expected)
    {
        var config = new PipelineConfig { Name = "x", Modules = [new() { Name = "x", Ecosystem = eco }] };
        var result = PipelineGenerator.Generate(config);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.Contains(expected, result.Files.Single(f => f.Path == "jenkins/stages/build.groovy").Content);
    }
}
