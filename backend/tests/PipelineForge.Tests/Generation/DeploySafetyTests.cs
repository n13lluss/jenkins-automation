using PipelineForge.Core.Generation;
using PipelineForge.Core.Model;

namespace PipelineForge.Tests.Generation;

/// <summary>Health checks, rollback, database migrations and application secrets.</summary>
public class DeploySafetyTests
{
    public static PipelineConfig Safe(AgentType agent = AgentType.Kubernetes, MigrationTool tool = MigrationTool.Flyway)
    {
        var c = WorkflowCoverageTests.SecureJava(agent);
        c.Migrations = new() { Tool = tool, Path = tool == MigrationTool.Flyway ? "src/main/resources/db/migration" : "db/changelog/db.changelog-master.yaml" };
        foreach (var e in c.Environments.Where(e => e.Key is "test" or "prod"))
        {
            e.Health = new() { Path = "/actuator/health", ExpectedStatus = 200, Successes = 3, TimeoutSeconds = 300 };
            e.Database = new() { JdbcUrl = $"jdbc:postgresql://db-{e.Key}.internal:5432/orders?sslmode=require", CredentialId = $"db-{e.Key}" };
        }
        c.Environments.Single(e => e.Key == "prod").AppSecrets = new()
        {
            StoreName = "vault",
            Keys = [new() { Name = "DB_PASSWORD", RemoteKey = "orders/prod/db", Property = "password" }, new() { Name = "API_KEY", RemoteKey = "orders/prod/api" }],
        };
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
    public void Deploys_check_health_and_roll_back(AgentType agent)
    {
        var f = Files(Safe(agent));
        var deploy = f["jenkins/stages/deploy.groovy"];
        Assert.Contains("healthCheck(common, cfg)", deploy);
        Assert.Contains("rollback(common, cfg, deployed)", deploy);
        Assert.Contains("helm rollback \"$DEPLOY_RELEASE\" 0", deploy);
        Assert.Contains("curl -s -o /dev/null -w '%{http_code}'", deploy);
        Assert.DoesNotContain("smokeTest", deploy);
        // The release is only rolled back by us once the upgrade itself went through.
        Assert.True(deploy.IndexOf("deployed = true", StringComparison.Ordinal) > deploy.IndexOf("deployHelm(common, cfg)", StringComparison.Ordinal));

        var envs = f["jenkins/config/environments.yaml"];
        Assert.Contains("path: \"/actuator/health\"", envs);
        Assert.Contains("rollback: true", envs);
        Assert.Contains("jdbcUrl: \"jdbc:postgresql://db-prod.internal:5432/orders?sslmode=require\"", envs);
    }

    [Fact]
    public void Flyway_gets_credentials_only_through_the_environment()
    {
        var f = Files(Safe());
        var deploy = f["jenkins/stages/deploy.groovy"];
        Assert.Contains("migrate(common, cfg)", deploy);
        Assert.Contains("usernameVariable: 'FLYWAY_USER', passwordVariable: 'FLYWAY_PASSWORD'", deploy);
        Assert.Contains("sh 'flyway migrate'", deploy);
        Assert.Contains("filesystem:${env.WORKSPACE}/src/main/resources/db/migration", deploy);
        Assert.Contains("flyway/flyway:", f["jenkins/agents/deploy-pod.yaml"]);
        Assert.Contains(PipelineGenerator.Generate(Safe()).Credentials, cr => cr.Id == "db-prod");
        // Migrations run before the release goes out.
        Assert.True(deploy.IndexOf("migrate(common, cfg)", StringComparison.Ordinal) < deploy.IndexOf("deployHelm(common, cfg)", StringComparison.Ordinal));
    }

    [Fact]
    public void Liquibase_splits_search_path_and_changelog()
    {
        var deploy = Files(Safe(tool: MigrationTool.Liquibase))["jenkins/stages/deploy.groovy"];
        Assert.Contains("LIQUIBASE_SEARCH_PATH=${env.WORKSPACE}/db/changelog", deploy);
        Assert.Contains("'LIQUIBASE_COMMAND_CHANGELOG_FILE=db.changelog-master.yaml'", deploy);
        Assert.Contains("sh 'liquibase update'", deploy);
    }

    [Fact]
    public void App_secrets_become_an_external_secret()
    {
        var f = Files(Safe());
        Assert.Contains("kind: ExternalSecret", f["deploy/helm/chart/templates/externalsecret.yaml"]);
        Assert.Contains("name: {{ $.Release.Name }}-app-secrets", f["deploy/helm/chart/templates/app.yaml"]);
        var values = f["deploy/helm/values-prod.yaml"];
        Assert.Contains("secretKey: \"DB_PASSWORD\"", values);
        Assert.Contains("remoteKey: \"orders/prod/db\"", values);
        Assert.Contains("property: \"password\"", values);
        Assert.DoesNotContain("externalSecret", f["deploy/helm/values-test.yaml"]);
    }

    [Fact]
    public void Docker_hosts_keep_the_previous_release()
    {
        var c = Safe(AgentType.Docker);
        foreach (var e in c.Environments)
        {
            e.Deploy.Type = DeployTargetType.DockerSsh;
            e.AppSecrets = null;
        }
        c.ClusterPolicy.Enabled = false;
        var deploy = Files(c)["jenkins/stages/deploy.groovy"];
        Assert.Contains("cp compose.yml compose.previous.yml && cp .env .env.previous", deploy);
        Assert.Contains("cp compose.previous.yml compose.yml && cp .env.previous .env", deploy);
        Assert.Contains("printf '%s\\\\n' \"IMAGE_TAG=$IMAGE_TAG\" \"IMAGE_DIGEST_API=$IMAGE_DIGEST_API\" > release.env", deploy);
        Assert.Contains("deployDockerSsh(common, cfg, 'rollback')", deploy);
    }

    [Fact]
    public void Azure_without_a_slot_records_the_previous_image()
    {
        var c = Safe();
        foreach (var e in c.Environments)
        {
            e.Deploy.Type = DeployTargetType.AzureAppService;
            e.AppSecrets = null;
        }
        c.ClusterPolicy.Enabled = false;
        var deploy = Files(c)["jenkins/stages/deploy.groovy"];
        Assert.Contains("--query linuxFxVersion --output tsv > \"reports/rollback-$DEPLOY_ENV.txt\"", deploy);
        Assert.Contains("'^DOCKER[|][A-Za-z0-9._/:@-]+$'", deploy);
    }

    [Fact]
    public void Groovy_stays_balanced()
    {
        foreach (var agent in new[] { AgentType.Kubernetes, AgentType.Docker })
            foreach (var tool in new[] { MigrationTool.Flyway, MigrationTool.Liquibase })
                foreach (var (path, content) in Files(Safe(agent, tool)).Where(kv => kv.Key.EndsWith(".groovy") || kv.Key.StartsWith("Jenkinsfile")))
                    Assert.True(PipelineForge.Core.Validation.ConfigValidator.BracesBalanced(content), $"{agent} {tool} {path}");
    }

    [Theory]
    [InlineData("jdbcUrl", "jdbc:postgresql://db/x\"; curl evil")]
    [InlineData("jdbcUrl", "jdbc:h2:mem:test")]
    [InlineData("jdbcUrl", "jdbc:postgresql://db/$(id)")]
    [InlineData("secretName", "db-password")]
    [InlineData("remoteKey", "../../etc/passwd")]
    [InlineData("remoteKey", "a b")]
    [InlineData("healthPath", "/health'; rm -rf /")]
    public void Injection_is_rejected(string field, string value)
    {
        var c = Safe();
        var prod = c.Environments.Single(e => e.Key == "prod");
        var i = c.Environments.IndexOf(prod);
        string path;
        switch (field)
        {
            case "jdbcUrl": prod.Database!.JdbcUrl = value; path = $"environments[{i}].database.jdbcUrl"; break;
            case "secretName": prod.AppSecrets!.Keys[0].Name = value; path = $"environments[{i}].appSecrets.keys"; break;
            case "remoteKey": prod.AppSecrets!.Keys[0].RemoteKey = value; path = $"environments[{i}].appSecrets.keys"; break;
            default: prod.Health.Path = value; path = $"environments[{i}].health.path"; break;
        }
        Assert.Contains(PipelineGenerator.Generate(c).Errors, e => e.Path == path);
    }

    [Fact]
    public void Dependent_settings_are_validated()
    {
        var c = Safe();
        c.Migrations.Tool = MigrationTool.None;
        c.Environments.Single(e => e.Key == "test").AppSecrets = new() { Keys = [new() { Name = "X", RemoteKey = "x" }] };
        c.Environments.Single(e => e.Key == "test").Deploy.Type = DeployTargetType.DockerSsh;
        var errors = PipelineGenerator.Generate(c).Errors.Select(e => e.Path).ToList();
        Assert.Contains("environments[1].database", errors);
        Assert.Contains("environments[1].appSecrets", errors);
    }

    [Fact]
    public void Compose_binds_where_configured_and_reads_the_host_env_file()
    {
        var c = Samples.MavenHelm();
        foreach (var e in c.Environments) e.Deploy.Type = DeployTargetType.DockerSsh;
        var test = c.Environments.Single(e => e.Key == "test").Deploy;
        test.BindAddress = "0.0.0.0";
        test.HostPort = 18080;
        test.EnvFile = "app.env";
        var compose = Files(c)["deploy/docker/compose.test.yml"];
        Assert.Contains("ports: [\"0.0.0.0:18080:8080\"]", compose);
        Assert.Contains("env_file: [\"app.env\"]", compose);
        Assert.Contains("tmpfs: [\"/tmp\"]", compose);
        Assert.Contains("127.0.0.1:8080:8080", Files(c)["deploy/docker/compose.dev.yml"]);

        test.BindAddress = "0.0.0.0; rm -rf /";
        test.EnvFile = "../../etc/shadow x";
        var errors = PipelineGenerator.Generate(c).Errors.Select(e => e.Path).ToList();
        Assert.Contains("environments[1].deploy.bindAddress", errors);
        Assert.Contains("environments[1].deploy.envFile", errors);
    }
}
