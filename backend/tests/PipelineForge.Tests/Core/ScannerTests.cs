using PipelineForge.Core.Generation;
using PipelineForge.Core.Model;
using PipelineForge.Core.Scanning;

namespace PipelineForge.Tests.Core;

public sealed class MemoryRepo(Dictionary<string, string> files) : IRepoContentReader
{
    public Task<IReadOnlyList<string>> ListFilesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(files.Keys.ToList());
    public Task<string?> ReadFileAsync(string path, CancellationToken ct) => Task.FromResult(files.GetValueOrDefault(path));
}

public class ScannerTests
{
    [Fact]
    public async Task Detects_a_maven_reactor_as_one_module_with_java_version_and_dockerfile()
    {
        var repo = new MemoryRepo(new()
        {
            ["pom.xml"] = "<project><properties><maven.compiler.release>17</maven.compiler.release></properties><modules><module>core</module></modules></project>",
            ["core/pom.xml"] = "<project/>",
            ["Dockerfile"] = "FROM eclipse-temurin:17",
            ["deploy/chart/Chart.yaml"] = "name: x",
        });
        var profile = await RepoScanner.ScanAsync(repo, "acme/Orders-Service");
        var m = Assert.Single(profile.Modules);
        Assert.Equal(Ecosystem.Maven, m.Ecosystem);
        Assert.Equal("17", m.LanguageVersion);
        Assert.Equal("Dockerfile", m.Dockerfile);
        Assert.Equal("acme-orders-service", m.Name);
        Assert.Equal(["deploy/chart"], profile.HelmCharts);
    }

    [Fact]
    public async Task Detects_a_polyglot_monorepo()
    {
        var repo = new MemoryRepo(new()
        {
            ["services/api/build.gradle.kts"] = "java { toolchain { languageVersion = JavaLanguageVersion.of(21) } }",
            ["services/api/Dockerfile"] = "FROM x",
            ["web/package.json"] = """{ "engines": { "node": ">=20.11" } }""",
            ["web/pnpm-lock.yaml"] = "",
            ["web/node_modules/left-pad/package.json"] = "{}",
            ["tools/cli/go.mod"] = "module x\n\ngo 1.23\n",
            ["worker/pyproject.toml"] = "requires-python = \">=3.12\"",
            ["billing/Billing.csproj"] = "<Project><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>",
            ["infra/main.tf"] = "",
            ["docker-compose.yml"] = "",
        });
        var profile = await RepoScanner.ScanAsync(repo, "shop");
        var byEco = profile.Modules.ToDictionary(m => m.Ecosystem);
        Assert.Equal(5, profile.Modules.Count);
        Assert.Equal("21", byEco[Ecosystem.Gradle].LanguageVersion);
        Assert.Equal("services/api/Dockerfile", byEco[Ecosystem.Gradle].Dockerfile);
        Assert.Equal("20", byEco[Ecosystem.Pnpm].LanguageVersion);
        Assert.Equal("1.23", byEco[Ecosystem.Go].LanguageVersion);
        Assert.Equal("3.12", byEco[Ecosystem.Python].LanguageVersion);
        Assert.Equal("9.0", byEco[Ecosystem.DotNet].LanguageVersion);
        Assert.True(profile.HasTerraform);
        Assert.True(profile.HasDockerCompose);
    }

    [Fact]
    public void Pom_parsing_refuses_external_entities()
    {
        const string xxe = """
            <?xml version="1.0"?>
            <!DOCTYPE project [ <!ENTITY xxe SYSTEM "file:///etc/passwd"> ]>
            <project><properties><java.version>&xxe;</java.version></properties></project>
            """;
        Assert.Null(RepoScanner.MavenJavaVersion(xxe));
    }

    [Fact]
    public async Task Suggestion_for_a_scanned_repo_generates()
    {
        var repo = new MemoryRepo(new()
        {
            ["package.json"] = "{}",
            ["package-lock.json"] = "{}",
            ["Dockerfile"] = "FROM node",
            ["k8s/deployment.yaml"] = "",
        });
        var profile = await RepoScanner.ScanAsync(repo, "acme/web-shop");
        var suggestion = SuggestionEngine.Suggest(profile, "acme/web-shop", "master");
        Assert.Equal("web-shop", suggestion.Config.Name);
        Assert.Equal(DeployTargetType.Helm, suggestion.Config.Environments[0].Deploy.Type);
        Assert.Equal("master", suggestion.Config.Environments.Single(e => e.Key == "prod").BranchPattern);
        Assert.Contains(DependencyScanner.NpmAudit, suggestion.Config.Security.DependencyScanners);
        var result = PipelineGenerator.Generate(suggestion.Config);
        Assert.True(result.Success, string.Join("\n", result.Errors));
    }
}
