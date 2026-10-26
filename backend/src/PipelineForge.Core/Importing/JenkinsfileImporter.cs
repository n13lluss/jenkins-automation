using System.Text.RegularExpressions;
using PipelineForge.Core.Model;
using PipelineForge.Core.Validation;

namespace PipelineForge.Core.Importing;

public sealed record ImportedStage(string Name, string MappedTo, bool Custom);

public sealed record ImportResult(
    PipelineConfig Config,
    IReadOnlyList<ImportedStage> Stages,
    IReadOnlyList<string> Gaps,
    IReadOnlyList<string> CredentialIds,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Best-effort import of a declarative Jenkinsfile. Known stages are mapped onto the model;
/// everything else is kept as custom stages so nothing is silently lost. Nothing is executed.
/// </summary>
public static partial class JenkinsfileImporter
{
    public const int MaxLength = 200_000;

    [GeneratedRegex(@"\bstage\s*\(\s*(['""])(.*?)\1\s*\)")] private static partial Regex StageRx();
    [GeneratedRegex(@"credentialsId\s*:\s*['""]([^'""]+)['""]|credentials\s*\(\s*['""]([^'""]+)['""]\s*\)")] private static partial Regex CredentialRx();
    [GeneratedRegex(@"\bbranch\s+(?:pattern\s*:\s*)?['""]([^'""]+)['""]")] private static partial Regex BranchRx();
    [GeneratedRegex(@"withSonarQubeEnv\s*\(\s*['""]([^'""]+)['""]")] private static partial Regex SonarEnvRx();
    [GeneratedRegex(@"sonar\.projectKey=([A-Za-z0-9_.:-]+)")] private static partial Regex SonarKeyRx();
    [GeneratedRegex(@"submitter\s*:\s*['""]([^'""]+)['""]")] private static partial Regex SubmitterRx();
    [GeneratedRegex(@"https?://[A-Za-z0-9._~:/?#@!&()*+,;=%-]+")] private static partial Regex UrlRx();
    [GeneratedRegex(@"\bdir\s*\(\s*['""]([^'""]+)['""]")] private static partial Regex DirRx();

    private sealed record Leaf(string Name, string Body, string? Steps, string? Branch);

    public static ImportResult Import(string jenkinsfile, string appName = "app")
    {
        if (jenkinsfile.Length > MaxLength) throw new ArgumentException("Jenkinsfile too large to import.");
        var g = new GroovyText(jenkinsfile);
        var warnings = new List<string>();
        var config = new PipelineConfig
        {
            Name = Scanning.SuggestionEngine.CleanName(appName),
            Quality = new QualityConfig { Enabled = false, WaitForQualityGate = false },
            Security = new SecurityConfig { SecretScan = false, DependencyScanners = [], Sbom = false, ContainerScan = false },
        };
        var stages = new List<ImportedStage>();

        var pipeline = g.Block("pipeline", 0, g.Text.Length);
        if (pipeline is null)
        {
            warnings.Add("No declarative `pipeline { }` block found (scripted pipelines are not supported); the file was kept as one custom stage.");
            if (ConfigValidator.BracesBalanced(g.Text) && g.Text.Length <= ConfigValidator.MaxCustomStageLength)
                config.CustomStages.Add(new CustomStage { Name = "Imported pipeline", Steps = $"script {{\n{g.Text.Trim()}\n}}", Origin = CustomStageOrigin.Imported });
            config.Modules.Add(new ModuleConfig { Name = config.Name, Ecosystem = Ecosystem.Maven });
            return new(config, stages, Gaps(config), Credentials(g.Text), warnings);
        }

        var body = g.Text[pipeline.Value.Start..pipeline.Value.End];
        if (Regex.IsMatch(body, @"\bkubernetes\s*\{")) config.Agent.Type = AgentType.Kubernetes;
        else if (Regex.IsMatch(body, @"\bdocker\s*\{|docker\.image\(")) config.Agent.Type = AgentType.Docker;
        var label = Regex.Match(body, @"\blabel\s+['""]([A-Za-z0-9._@-]+)['""]");
        if (label.Success && config.Agent.Type == AgentType.Kubernetes && !body.Contains("kubernetes"))
        {
            config.Agent.Type = AgentType.Docker;
            config.Agent.DockerLabel = label.Groups[1].Value;
        }

        var leaves = new List<Leaf>();
        var top = g.Block("stages", pipeline.Value.Start, pipeline.Value.End);
        if (top is not null) CollectStages(g, top.Value, null, leaves);
        else warnings.Add("No `stages { }` block found.");

        var modules = new Dictionary<string, ModuleConfig>();
        var envOrder = 0;
        foreach (var leaf in leaves)
        {
            var text = leaf.Body;
            var mapped = new List<string>();

            void Module(Ecosystem e)
            {
                var path = DirRx().Match(text) is { Success: true } d && ConfigValidator.IsRelativePath(d.Groups[1].Value) ? d.Groups[1].Value : ".";
                if (modules.ContainsKey(path)) return;
                var name = path == "." ? config.Name : Scanning.SuggestionEngine.CleanName(path);
                modules[path] = new ModuleConfig { Name = name, Path = path, Ecosystem = e };
                mapped.Add($"build ({e})");
            }

            var isSonar = Has(text, @"withSonarQubeEnv|sonar-scanner|sonar:sonar|sonarscanner|\bsonar\b");
            var isSecurity = Has(text, @"gitleaks|trufflehog|dependency-check|trivy|osv-scanner|\bsnyk\b|npm audit|cyclonedx|checkov|tfsec|zap-");
            var isDeploy = Has(text, @"helm\s+upgrade|kubectl\s+apply|az\s+webapp|docker[ -]compose|\bssh\b");
            if (!isSonar && !isSecurity && !isDeploy)
            {
                if (Has(text, @"\bmvnw?\b")) Module(Ecosystem.Maven);
                else if (Has(text, @"gradlew|\bgradle\b")) Module(Ecosystem.Gradle);
                else if (Has(text, @"\bpnpm\b")) Module(Ecosystem.Pnpm);
                else if (Has(text, @"\byarn\b")) Module(Ecosystem.Yarn);
                else if (Has(text, @"\bnpm\b")) Module(Ecosystem.Npm);
                else if (Has(text, @"\bdotnet\b")) Module(Ecosystem.DotNet);
                else if (Has(text, @"\bpytest\b|\bpip3?\b|\bpoetry\b")) Module(Ecosystem.Python);
                else if (Has(text, @"\bgo\s+(build|test|vet)\b")) Module(Ecosystem.Go);
                else if (Has(text, @"\bcomposer\b|phpunit")) Module(Ecosystem.Composer);
            }

            if (isSonar)
            {
                config.Quality.Enabled = true;
                if (SonarEnvRx().Match(text) is { Success: true } s) config.Quality.ServerName = s.Groups[1].Value;
                if (SonarKeyRx().Match(text) is { Success: true } k) config.Quality.ProjectKey = k.Groups[1].Value;
                if (Has(text, "sonarcloud")) config.Quality.Platform = QualityPlatform.SonarCloud;
                mapped.Add("Sonar analysis");
            }
            if (Has(text, "waitForQualityGate"))
            {
                config.Quality.Enabled = true;
                config.Quality.WaitForQualityGate = true;
                mapped.Add("quality gate");
            }
            if (Has(text, "gitleaks|trufflehog")) { config.Security.SecretScan = true; mapped.Add("secret scan"); }
            void Scanner(DependencyScanner s, string pattern)
            {
                if (!Has(text, pattern)) return;
                if (!config.Security.DependencyScanners.Contains(s)) config.Security.DependencyScanners.Add(s);
                mapped.Add($"dependency scan ({s})");
            }
            Scanner(DependencyScanner.OwaspDependencyCheck, "dependency-check");
            Scanner(DependencyScanner.TrivyFs, @"trivy\s+(fs|filesystem|repo)");
            Scanner(DependencyScanner.OsvScanner, "osv-scanner");
            Scanner(DependencyScanner.Snyk, @"\bsnyk\b");
            Scanner(DependencyScanner.NpmAudit, @"npm\s+audit");
            if (Has(text, @"cyclonedx|sbom")) { config.Security.Sbom = true; mapped.Add("SBOM"); }
            if (Has(text, @"trivy\s+image|grype")) { config.Security.ContainerScan = true; mapped.Add("container scan"); }
            if (Has(text, @"trivy\s+config|checkov|tfsec|kics")) { config.Security.IacScan = true; mapped.Add("IaC scan"); }
            if (Has(text, @"zap-(baseline|full-scan|api-scan)|owasp/zap|zaproxy"))
            {
                config.Security.Dast.Enabled = true;
                config.Security.Dast.Mode = Has(text, "zap-full-scan") ? DastMode.Full : Has(text, "zap-api-scan") ? DastMode.Api : DastMode.Baseline;
                mapped.Add("DAST");
            }
            if (Has(text, @"docker\s+build|kaniko|buildah|docker\.build")) mapped.Add("image build");

            if (isDeploy)
            {
                var type = Has(text, @"helm\s+upgrade|kubectl") ? DeployTargetType.Helm
                    : Has(text, @"az\s+webapp") ? DeployTargetType.AzureAppService
                    : DeployTargetType.DockerSsh;
                var key = EnvironmentKey(leaf.Name) ?? $"env{++envOrder}";
                if (config.Environments.All(e => e.Key != key))
                {
                    var env = new EnvironmentConfig
                    {
                        Key = key,
                        DisplayName = DisplayFor(key),
                        BranchPattern = leaf.Branch is { } b && ConfigValidator.IsBranchPattern(b) ? b : (key == "prod" ? "main" : "develop"),
                        RequiresApproval = Has(text, @"\binput\b"),
                        Url = UrlRx().Match(text) is { Success: true } u && ConfigValidator.IsHttpUrl(u.Value) ? u.Value : null,
                        Deploy = new DeployTarget { Type = type },
                    };
                    config.Environments.Add(env);
                }
                mapped.Add($"deploy {key} ({type})");
            }
            if (!isDeploy && Has(text, @"\binput\b") && EnvironmentKey(leaf.Name) is { } approvalKey)
            {
                if (SubmitterRx().Match(text) is { Success: true } sub && ConfigValidator.IsIdentifier(sub.Groups[1].Value))
                    config.Compliance.ApproverGroup = sub.Groups[1].Value;
                mapped.Add($"approval {approvalKey}");
            }

            if (mapped.Count == 0)
            {
                var steps = leaf.Steps ?? "";
                if (steps.Length > 0 && steps.Length <= ConfigValidator.MaxCustomStageLength && ConfigValidator.BracesBalanced(steps)
                    && Regex.IsMatch(leaf.Name, @"^[A-Za-z0-9 ._()-]{1,40}$"))
                {
                    config.CustomStages.Add(new CustomStage { Name = leaf.Name, Steps = Dedent(steps), Origin = CustomStageOrigin.Imported });
                    stages.Add(new(leaf.Name, "custom stage", true));
                }
                else
                {
                    warnings.Add($"Stage '{leaf.Name}' could not be imported; add it by hand.");
                    stages.Add(new(leaf.Name, "not imported", true));
                }
            }
            else
            {
                stages.Add(new(leaf.Name, string.Join(", ", mapped.Distinct()), false));
            }
        }

        // Approval stages that preceded a deploy stage apply to that environment.
        foreach (var approval in stages.Where(s => s.MappedTo.StartsWith("approval ")))
        {
            var key = approval.MappedTo["approval ".Length..];
            if (config.Environments.FirstOrDefault(e => e.Key == key) is { } env) env.RequiresApproval = true;
        }

        config.Modules = modules.Values.ToList();
        if (config.Modules.Count == 0)
        {
            config.Modules.Add(new ModuleConfig { Name = config.Name, Path = ".", Ecosystem = Ecosystem.Maven });
            warnings.Add("No build tool recognised; a Maven module was added as a placeholder.");
        }
        if (config.Security.Dast.Enabled)
        {
            var target = config.Environments.FirstOrDefault(e => e.Key is "test" or "acc" && e.Url is not null)
                         ?? config.Environments.FirstOrDefault(e => e.Url is not null);
            config.Security.Dast.Environments = target is null ? [] : [target.Key];
            if (target is null) { config.Security.Dast.Enabled = false; warnings.Add("DAST found but no environment URL; set one to re-enable it."); }
        }
        if (Has(body, @"disableConcurrentBuilds")) config.Options.DisableConcurrentBuilds = true;
        return new(config, stages, Gaps(config), Credentials(g.Text), warnings);
    }

    private static void CollectStages(GroovyText g, (int Start, int End) range, string? inheritedBranch, List<Leaf> leaves)
    {
        var i = range.Start;
        while (i < range.End)
        {
            var m = StageRx().Match(g.Text, i);
            if (!m.Success || m.Index >= range.End) return;
            if (!g.IsCode(m.Index)) { i = m.Index + 1; continue; }
            var open = m.Index + m.Length;
            while (open < range.End && char.IsWhiteSpace(g.Text[open])) open++;
            if (open >= range.End || g.Text[open] != '{') { i = m.Index + m.Length; continue; }
            var close = g.MatchBrace(open);
            if (close < 0 || close > range.End) return;
            var inner = (open + 1, close);
            var text = g.Slice(inner);
            var whenBlock = g.Block("when", inner.Item1, inner.Item2);
            var branch = whenBlock is null ? inheritedBranch : BranchRx().Match(g.Slice(whenBlock.Value)) is { Success: true } b ? b.Groups[1].Value : inheritedBranch;

            var nested = g.Block("stages", inner.Item1, inner.Item2) ?? g.Block("parallel", inner.Item1, inner.Item2);
            if (nested is not null)
            {
                CollectStages(g, nested.Value, branch, leaves);
            }
            else
            {
                var steps = g.Block("steps", inner.Item1, inner.Item2);
                leaves.Add(new Leaf(m.Groups[2].Value, text, steps is null ? null : g.Slice(steps.Value), branch));
            }
            i = close + 1;
        }
    }

    private static bool Has(string text, string pattern) => Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);

    private static string? EnvironmentKey(string stageName)
    {
        var n = stageName.ToLowerInvariant();
        if (Regex.IsMatch(n, @"\b(prod|production|prd|live)\b")) return "prod";
        if (Regex.IsMatch(n, @"\b(acc|acceptance|accept|uat|staging|stage|preprod)\b")) return "acc";
        if (Regex.IsMatch(n, @"\b(test|tst|qa)\b")) return "test";
        if (Regex.IsMatch(n, @"\b(dev|development|ontwikkel)\b")) return "dev";
        return null;
    }

    private static string DisplayFor(string key) => key switch
    {
        "dev" => "Development",
        "test" => "Test",
        "acc" => "Acceptance",
        "prod" => "Production",
        _ => key,
    };

    private static IReadOnlyList<string> Credentials(string text) =>
        CredentialRx().Matches(text)
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
            .Where(ConfigValidator.IsIdentifier)
            .Distinct()
            .ToList();

    private static string Dedent(string text)
    {
        var lines = text.Replace("\r\n", "\n").Trim('\n').Split('\n');
        var indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        return string.Join("\n", lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart())).Trim();
    }

    public static IReadOnlyList<string> Gaps(PipelineConfig c)
    {
        var gaps = new List<string>();
        if (!c.Security.SecretScan) gaps.Add("No secret scanning (e.g. Gitleaks).");
        if (c.Security.DependencyScanners.Count == 0) gaps.Add("No dependency vulnerability scan.");
        if (!c.Quality.Enabled) gaps.Add("No static analysis (SonarQube/SonarCloud).");
        else if (!c.Quality.WaitForQualityGate) gaps.Add("Sonar runs, but the quality gate does not block the pipeline.");
        if (!c.Security.Sbom) gaps.Add("No SBOM is produced.");
        if (!c.Security.ContainerScan && c.Modules.Any(m => m.Dockerfile is not null)) gaps.Add("Images are not scanned before they are pushed.");
        if (!c.Security.Dast.Enabled && c.Environments.Count > 0) gaps.Add("No DAST against a deployed environment.");
        var prod = c.Environments.FirstOrDefault(Compliance.ComplianceEnforcer.IsProduction);
        if (prod is not null && !prod.RequiresApproval) gaps.Add($"{prod.DisplayName} deploys without a manual approval.");
        if (c.Environments.Count == 0) gaps.Add("No DTAP environments or deploy stages.");
        return gaps;
    }
}
