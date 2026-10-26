using System.Text.Json;
using PipelineForge.Core.Compliance;
using PipelineForge.Core.Model;
using PipelineForge.Core.Validation;

namespace PipelineForge.Core.Generation;

public sealed record GeneratedFile(string Path, string Content);

public sealed record GenerationResult(
    IReadOnlyList<GeneratedFile> Files,
    IReadOnlyList<ValidationError> Errors,
    ComplianceResult Compliance,
    IReadOnlyList<RequiredCredential> Credentials,
    IReadOnlyList<string> Plugins,
    PipelineConfig EffectiveConfig)
{
    public bool Success => Errors.Count == 0;
}

public static class PipelineGenerator
{
    public const string DefaultChartPath = "deploy/helm/chart";

    /// <param name="config">Not modified; compliance changes are applied to a copy.</param>
    /// <param name="onlyFiles">Return just these paths (all files when null or empty).</param>
    public static GenerationResult Generate(PipelineConfig config, IReadOnlyCollection<string>? onlyFiles = null)
    {
        var c = Clone(config);
        var compliance = ComplianceEnforcer.Apply(c);
        var errors = ConfigValidator.Validate(c);
        if (errors.Count > 0) return new([], errors, compliance, [], [], c);

        var ctx = new GenerationContext(c);
        var files = new List<GeneratedFile>();
        void Add(string path, string content) => files.Add(new(path, content));

        Add("Jenkinsfile", JenkinsfileWriter.Write(ctx));
        Add("jenkins/config/environments.yaml", SupportFilesWriter.EnvironmentsYaml(ctx));
        if (ctx.IsKubernetes)
        {
            Add("jenkins/agents/ci-pod.yaml", SupportFilesWriter.PodTemplate(ctx, ctx.CiContainers, "CI"));
            if (ctx.DeployContainers.Count > 0)
                Add("jenkins/agents/deploy-pod.yaml", SupportFilesWriter.PodTemplate(ctx, ctx.DeployContainers, "Deploy/DAST"));
        }
        Add("jenkins/stages/common.groovy", StageScriptsWriter.Common(ctx));
        Add("jenkins/stages/build.groovy", StageScriptsWriter.Build(ctx));
        if (c.Quality.Enabled) Add("jenkins/stages/quality.groovy", StageScriptsWriter.Quality(ctx));
        Add("jenkins/stages/security.groovy", StageScriptsWriter.Security(ctx));
        if (ctx.ImageModules.Count > 0 || ctx.DeployEnvironments.Count > 0) Add("jenkins/stages/deploy.groovy", StageScriptsWriter.Deploy(ctx));
        if (ctx.HasDast) Add("jenkins/stages/dast.groovy", StageScriptsWriter.Dast(ctx));
        if (c.AiReview.Enabled)
        {
            Add("jenkins/stages/ai-review.groovy", StageScriptsWriter.AiReview(ctx));
            Add("jenkins/scripts/ai_review.py", AiReviewScript.Python);
        }
        if (c.Quality.Enabled && ctx.SonarMode == SonarMode.Cli) Add("sonar-project.properties", SupportFilesWriter.SonarProperties(ctx));
        if (c.Security.SecretScan) Add("security/.gitleaks.toml", SupportFilesWriter.GitleaksToml);
        if (ctx.UsesTrivy) Add("security/.trivyignore", SupportFilesWriter.TrivyIgnore);
        if (c.Security.DependencyScanners.Contains(DependencyScanner.OwaspDependencyCheck))
            Add("security/dependency-check-suppressions.xml", SupportFilesWriter.DependencyCheckSuppressions);
        if (ctx.HasDast) Add("security/zap-rules.tsv", SupportFilesWriter.ZapRules(ctx));

        var helmEnvs = ctx.DeployEnvironments.Where(e => e.Deploy.Type == DeployTargetType.Helm).ToList();
        foreach (var e in helmEnvs) Add($"deploy/helm/values-{e.Key}.yaml", SupportFilesWriter.HelmValues(ctx, e));
        if (helmEnvs.Any(e => e.Deploy.ChartPath == DefaultChartPath))
        {
            Add($"{DefaultChartPath}/Chart.yaml", SupportFilesWriter.HelmChartYaml(ctx));
            Add($"{DefaultChartPath}/templates/app.yaml", SupportFilesWriter.HelmDeploymentTemplate);
        }
        foreach (var e in ctx.DeployEnvironments.Where(e => e.Deploy.Type == DeployTargetType.DockerSsh))
            Add($"deploy/docker/compose.{e.Key}.yml", SupportFilesWriter.Compose(ctx, e));

        if (ctx.IsIso) Add("docs/iso27001-evidence.md", DocsWriter.IsoEvidence(ctx));
        var paths = files.Select(f => f.Path).Append("docs/PIPELINE.md").ToList();
        Add("docs/PIPELINE.md", DocsWriter.Pipeline(ctx, paths));

        if (onlyFiles is { Count: > 0 })
        {
            var wanted = onlyFiles.ToHashSet(StringComparer.Ordinal);
            files = files.Where(f => wanted.Contains(f.Path)).ToList();
        }
        return new(files, [], compliance, DocsWriter.RequiredCredentials(ctx), DocsWriter.RequiredPlugins(ctx), c);
    }

    public static PipelineConfig Clone(PipelineConfig config) =>
        JsonSerializer.Deserialize<PipelineConfig>(JsonSerializer.Serialize(config, JsonDefaults.Options), JsonDefaults.Options)!;
}
