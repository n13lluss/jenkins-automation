using PipelineForge.Core.Model;

namespace PipelineForge.Tests;

public static class Samples
{
    public static PipelineConfig MavenHelm() => new()
    {
        Name = "orders",
        Modules = [new() { Name = "api", Path = ".", Ecosystem = Ecosystem.Maven, LanguageVersion = "21", Dockerfile = "Dockerfile", ImageName = "shop/orders-api" }],
        Registry = new() { Host = "registry.example.com" },
        Quality = new() { ProjectKey = "shop_orders" },
        Security = new()
        {
            DependencyScanners = [DependencyScanner.TrivyFs, DependencyScanner.OwaspDependencyCheck],
            Dast = new() { Enabled = true, Environments = ["test"] },
        },
        Environments =
        [
            Env("dev", "Development", "develop", null),
            Env("test", "Test", "develop", "https://orders.test.example.com"),
            Env("acc", "Acceptance", "release/*", "https://orders.acc.example.com"),
            Env("prod", "Production", "main", "https://orders.example.com", approval: true),
        ],
        AiReview = new() { Enabled = true },
    };

    public static EnvironmentConfig Env(string key, string name, string branch, string? url, bool approval = false,
        DeployTargetType type = DeployTargetType.Helm) => new()
    {
        Key = key, DisplayName = name, BranchPattern = branch, Url = url, RequiresApproval = approval,
        Deploy = new() { Type = type, Namespace = $"orders-{key}", ReleaseName = "orders", Host = "deploy.example.com", ResourceGroup = "rg-orders", AppName = $"orders-{key}" },
    };

    public static PipelineConfig Monorepo() => new()
    {
        Name = "shop",
        Agent = new() { Type = AgentType.Docker },
        Modules =
        [
            new() { Name = "backend", Path = "backend", Ecosystem = Ecosystem.Gradle, Dockerfile = "backend/Dockerfile", ImageName = "shop/backend" },
            new() { Name = "web", Path = "web", Ecosystem = Ecosystem.Pnpm, Dockerfile = "web/Dockerfile", ImageName = "shop/web" },
            new() { Name = "worker", Path = "worker", Ecosystem = Ecosystem.Python },
        ],
        Registry = new() { Host = "registry.example.com" },
        Quality = new() { Platform = QualityPlatform.SonarCloud, Organization = "acme", ProjectKey = "acme_shop" },
        Security = new() { DependencyScanners = [DependencyScanner.OsvScanner, DependencyScanner.NpmAudit], LicenseCheck = true, IacScan = true },
        Environments =
        [
            Env("test", "Test", "develop", "https://shop.test.example.com", type: DeployTargetType.DockerSsh),
            Env("prod", "Production", "main", null, approval: true, type: DeployTargetType.AzureAppService),
        ],
        Compliance = new() { Profile = ComplianceProfile.Iso27001 },
    };
}
