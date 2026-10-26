using System.Xml.Linq;
using PipelineForge.Core.Generation;
using PipelineForge.Core.Model;
using PipelineForge.Core.Scm;
using PipelineForge.Infrastructure.Jenkins;

namespace PipelineForge.Tests.Generation;

public class SharedLibraryTests
{
    public static PipelineConfig Library(AgentType agent = AgentType.Kubernetes)
    {
        var c = SupplyChainTests.Promoting(agent);
        c.Name = "orders-api";
        c.Layout = PipelineLayout.SharedLibrary;
        c.Library = new() { Name = "acme-pipelines", Repository = "acme/jenkins-library", DefaultVersion = "main" };
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
    public void App_repository_keeps_only_a_thin_jenkinsfile(AgentType agent)
    {
        var f = Files(Library(agent));

        Assert.Contains("@Library('acme-pipelines') _", f["Jenkinsfile"]);
        Assert.Contains("pfOrdersApi()", f["Jenkinsfile"]);
        Assert.Contains("pfOrdersApiScheduled()", f["Jenkinsfile.scheduled"]);
        // Pipeline-owned files leave the app repository.
        Assert.DoesNotContain(f.Keys, p => p.StartsWith("jenkins/", StringComparison.Ordinal));
        Assert.DoesNotContain("security/.hadolint.yaml", f.Keys);
        Assert.DoesNotContain("security/zap/auth-plan.yaml", f.Keys);
        // App-owned files stay.
        Assert.Contains("deploy/helm/values-prod.yaml", f.Keys);
        Assert.Contains("security/.trivyignore", f.Keys);

        const string root = "library/resources/pipelineforge/orders-api/";
        Assert.Contains(root + "jenkins/stages/deploy.groovy", f.Keys);
        Assert.Contains(root + "jenkins/config/environments.yaml", f.Keys);
        Assert.Contains(root + "security/.hadolint.yaml", f.Keys);
        Assert.Contains("library/README.md", f.Keys);
    }

    [Fact]
    public void Library_pipeline_loads_its_files_from_the_library()
    {
        var f = Files(Library());
        var pipeline = f["library/vars/pfOrdersApi.groovy"];
        Assert.Contains("@Field def common", pipeline);
        Assert.Contains("def call() {", pipeline);
        Assert.Contains("yaml libraryResource('pipelineforge/orders-api/jenkins/agents/ci-pod.yaml')", pipeline);
        Assert.DoesNotContain("yamlFile", pipeline);
        // Every stage on a new agent first restores the library copies, then loads them.
        var restores = pipeline.Split("pfOrdersApiFiles()").Length - 1;
        var deploys = pipeline.Split("delivery.deploy(").Length - 1;
        Assert.True(restores > deploys, $"{restores} restores for {deploys} deploys");
        Assert.True(pipeline.IndexOf("pfOrdersApiFiles()", StringComparison.Ordinal) < pipeline.IndexOf("load 'jenkins/stages/common.groovy'", StringComparison.Ordinal));

        var files = f["library/vars/pfOrdersApiFiles.groovy"];
        Assert.Contains("'jenkins/stages/common.groovy',", files);
        Assert.Contains("'jenkins/config/environments.yaml',", files);
        Assert.Contains("'security/cosign.pub',", files);
        Assert.DoesNotContain("jenkins/agents/", files);
        Assert.Contains("libraryResource('pipelineforge/orders-api/' + path)", files);

        Assert.Contains("yaml libraryResource(", f["library/vars/pfOrdersApiScheduled.groovy"]);
        Assert.Contains("pfOrdersApiFiles()", f["library/vars/pfOrdersApiScheduled.groovy"]);
    }

    [Fact]
    public void Library_groovy_stays_balanced()
    {
        foreach (var agent in new[] { AgentType.Kubernetes, AgentType.Docker })
            foreach (var (path, content) in Files(Library(agent)).Where(kv => kv.Key.StartsWith("Jenkinsfile") || kv.Key.EndsWith(".groovy")))
                Assert.True(PipelineForge.Core.Validation.ConfigValidator.BracesBalanced(content), $"{agent} {path}");
    }

    [Fact]
    public void In_repo_layout_is_unchanged_and_warned_about()
    {
        var f = Files(Samples.MavenHelm());
        Assert.Contains("yamlFile 'jenkins/agents/ci-pod.yaml'", f["Jenkinsfile"]);
        Assert.DoesNotContain("@Field", f["Jenkinsfile"]);
        Assert.DoesNotContain(f.Keys, p => p.StartsWith(LibraryWriter.Prefix, StringComparison.Ordinal));
        Assert.Contains("in-repository layout", f["docs/PIPELINE.md"]);
    }

    [Theory]
    [InlineData("name", "lib'); evil('")]
    [InlineData("defaultVersion", "release/*")]
    [InlineData("defaultVersion", "../main")]
    [InlineData("repository", "acme/lib/../x")]
    [InlineData("repository", "noslash")]
    public void Library_settings_are_validated(string field, string value)
    {
        var c = Library();
        switch (field)
        {
            case "name": c.Library.Name = value; break;
            case "defaultVersion": c.Library.DefaultVersion = value; break;
            default: c.Library.Repository = value; break;
        }
        Assert.Contains(PipelineGenerator.Generate(c).Errors, e => e.Path == $"library.{field}");
    }

    [Theory]
    [InlineData(ScmKind.GitHub, "ForkPullRequestDiscoveryTrait$TrustPermission")]
    [InlineData(ScmKind.BitbucketCloud, "ForkPullRequestDiscoveryTrait$TrustTeamForks")]
    public void Job_registers_the_folder_library_and_trusts_only_known_forks(ScmKind scm, string trust)
    {
        var xml = JenkinsClient.JobXml(new MultibranchJob("orders", scm, "acme/orders", "scm-cred", null)
        {
            Library = new JobLibrary("acme-pipelines", "acme/jenkins-library", "main"),
        });
        var doc = XDocument.Parse(xml);
        Assert.Contains(trust, xml);
        var lib = doc.Descendants("org.jenkinsci.plugins.workflow.libs.LibraryConfiguration").Single();
        Assert.Equal("acme-pipelines", lib.Element("name")!.Value);
        Assert.Equal("false", lib.Element("allowVersionOverride")!.Value);
        Assert.Equal("jenkins-library", lib.Descendants("repository").Single().Value);
        // The library source only discovers branches.
        Assert.DoesNotContain("PullRequest", lib.ToString());
    }
}
