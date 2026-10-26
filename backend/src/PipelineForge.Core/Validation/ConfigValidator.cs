using System.Text.RegularExpressions;
using PipelineForge.Core.Model;

namespace PipelineForge.Core.Validation;

public sealed record ValidationError(string Path, string Message);

/// <summary>
/// Allow-list validation for every value that ends up in a generated file.
/// The generator refuses to render a config that does not pass.
/// </summary>
public static partial class ConfigValidator
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._@-]{0,127}$")] private static partial Regex IdentifierRx();
    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]{0,62}$")] private static partial Regex NameRx();
    [GeneratedRegex(@"^[a-z0-9][a-z0-9._/-]{0,127}$")] private static partial Regex ImageNameRx();
    [GeneratedRegex(@"^[a-z][a-z0-9-]{0,15}$")] private static partial Regex EnvKeyRx();
    [GeneratedRegex(@"^[A-Za-z0-9 ._()-]{1,40}$")] private static partial Regex DisplayNameRx();
    [GeneratedRegex(@"^[A-Za-z0-9._/*-]{1,200}$")] private static partial Regex BranchRx();
    [GeneratedRegex(@"^[A-Za-z0-9._/-]{1,200}$")] private static partial Regex RelPathRx();
    [GeneratedRegex(@"^/[A-Za-z0-9._/-]{0,200}$")] private static partial Regex AbsPathRx();
    [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9.-]{0,252}[A-Za-z0-9])?(:[0-9]{1,5})?$")] private static partial Regex HostRx();
    [GeneratedRegex(@"^[a-z0-9]([-a-z0-9]{0,61}[a-z0-9])?$")] private static partial Regex K8sNameRx();
    [GeneratedRegex(@"^[a-z_][a-z0-9_-]{0,31}$")] private static partial Regex UnixUserRx();
    [GeneratedRegex(@"^[A-Za-z0-9._()-]{1,90}$")] private static partial Regex AzureRgRx();
    [GeneratedRegex(@"^[A-Za-z0-9-]{1,60}$")] private static partial Regex AzureAppRx();
    [GeneratedRegex(@"^[A-Za-z0-9._:/-]{1,100}$")] private static partial Regex ModelRx();
    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,20}$")] private static partial Regex VersionRx();
    [GeneratedRegex(@"^[A-Za-z0-9_.:-]{1,400}$")] private static partial Regex SonarKeyRx();
    [GeneratedRegex(@"^[a-z0-9_-]{1,255}$")] private static partial Regex SonarOrgRx();
    [GeneratedRegex(@"^[A-Za-z0-9._:/@-]{1,255}$")] private static partial Regex ImageRefRx();
    [GeneratedRegex(@"^[H0-9*/,\- ]{9,60}$")] private static partial Regex CronRx();
    [GeneratedRegex(@"^[A-Z][A-Z0-9_]{1,9}$")] private static partial Regex JiraKeyRx();
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9 _-]{0,39}$")] private static partial Regex IssueTypeRx();
    [GeneratedRegex(@"^/[A-Za-z0-9._~/%-]{0,200}$")] private static partial Regex UrlPathRx();
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.-]{0,63}$")] private static partial Regex FieldRx();
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")] private static partial Regex LibraryNameRx();
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}/[A-Za-z0-9][A-Za-z0-9._-]{0,99}$")] private static partial Regex RepositoryRx();
    [GeneratedRegex(@"^[A-Z_][A-Z0-9_]{0,63}=[A-Za-z0-9._:/,@=-]{0,200}$")] private static partial Regex AppEnvRx();

    public const int MaxCustomStageLength = 20_000;

    public static bool IsIdentifier(string? v) => v is not null && IdentifierRx().IsMatch(v);
    public static bool IsName(string? v) => v is not null && NameRx().IsMatch(v);
    public static bool IsRepository(string? v) => v is not null && RepositoryRx().IsMatch(v) && !v.Contains("..");
    public static bool IsBranchPattern(string? v) => v is not null && BranchRx().IsMatch(v) && !v.Contains("..");
    public static bool IsHost(string? v) => v is not null && HostRx().IsMatch(v);

    public static bool IsRelativePath(string? v)
    {
        if (v is null || !RelPathRx().IsMatch(v) || v.StartsWith('/')) return false;
        return v.Split('/').All(seg => seg != ".." && seg.Length > 0);
    }

    public static bool IsAbsolutePath(string? v) =>
        v is not null && AbsPathRx().IsMatch(v) && v.Split('/').All(seg => seg != "..");

    public static bool IsHttpUrl(string? v)
    {
        if (string.IsNullOrEmpty(v) || v.Length > 2048) return false;
        if (v.Any(c => char.IsWhiteSpace(c) || c is '\'' or '"' or '`' or '\\' or '$' or '<' or '>')) return false;
        return Uri.TryCreate(v, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && string.IsNullOrEmpty(uri.UserInfo);
    }

    public static IReadOnlyList<ValidationError> Validate(PipelineConfig c)
    {
        var errors = new List<ValidationError>();
        void Check(bool ok, string path, string message) { if (!ok) errors.Add(new(path, message)); }

        Check(IsName(c.Name), "name", "Lowercase letters, digits, '.', '_' and '-' only (max 63).");
        if (c.Layout == PipelineLayout.SharedLibrary)
        {
            Check(LibraryNameRx().IsMatch(c.Library.Name), "library.name", "Letters, digits, '.', '_' and '-' only.");
            Check(IsBranchPattern(c.Library.DefaultVersion) && !c.Library.DefaultVersion.Contains('*'), "library.defaultVersion", "A branch or tag such as main or v1.");
            if (c.Library.Repository is { } repo) Check(IsRepository(repo), "library.repository", "Use owner/name (Bitbucket Server: PROJECT/repo).");
        }

        Check(IsIdentifier(c.Agent.DockerLabel), "agent.dockerLabel", "Invalid Jenkins label.");
        if (c.Agent.KubernetesCloud is { } cloud) Check(IsIdentifier(cloud), "agent.kubernetesCloud", "Invalid cloud name.");
        foreach (var (tool, image) in c.Agent.ImageOverrides)
        {
            Check(IsName(tool), $"agent.imageOverrides.{tool}", "Invalid tool name.");
            Check(ImageRefRx().IsMatch(image), $"agent.imageOverrides.{tool}", "Invalid image reference.");
        }

        Check(c.Modules.Count is > 0 and <= 20, "modules", "Between 1 and 20 modules are required.");
        Check(c.Modules.Select(m => m.Name).Distinct().Count() == c.Modules.Count, "modules", "Module names must be unique.");
        for (var i = 0; i < c.Modules.Count; i++)
        {
            var m = c.Modules[i];
            var p = $"modules[{i}]";
            Check(IsName(m.Name), $"{p}.name", "Invalid module name.");
            Check(m.Path == "." || IsRelativePath(m.Path), $"{p}.path", "Must be a relative path inside the repository.");
            if (m.LanguageVersion is { } lv) Check(VersionRx().IsMatch(lv), $"{p}.languageVersion", "Invalid version.");
            if (m.Dockerfile is { } df)
            {
                Check(IsRelativePath(df), $"{p}.dockerfile", "Must be a relative path inside the repository.");
                Check(m.ImageName is not null && ImageNameRx().IsMatch(m.ImageName), $"{p}.imageName", "An image name is required with a Dockerfile.");
            }
        }

        if (c.Registry is { } reg)
        {
            Check(IsHost(reg.Host), "registry.host", "Invalid registry host.");
            Check(IsIdentifier(reg.CredentialId), "registry.credentialId", "Invalid credential ID.");
        }
        Check(c.Registry is not null || c.Modules.All(m => m.Dockerfile is null), "registry", "A registry is required when a module builds an image.");

        var q = c.Quality;
        if (q.Enabled)
        {
            Check(IsIdentifier(q.ServerName), "quality.serverName", "Invalid SonarQube installation name.");
            Check(SonarKeyRx().IsMatch(q.ProjectKey), "quality.projectKey", "Invalid Sonar project key.");
            if (q.Platform == QualityPlatform.SonarCloud)
                Check(q.Organization is not null && SonarOrgRx().IsMatch(q.Organization), "quality.organization", "SonarCloud requires an organisation key.");
        }

        var s = c.Security;
        Check(IsIdentifier(s.NvdApiKeyCredentialId), "security.nvdApiKeyCredentialId", "Invalid credential ID.");
        Check(IsIdentifier(s.SnykCredentialId), "security.snykCredentialId", "Invalid credential ID.");
        Check(s.DependencyScanners.Distinct().Count() == s.DependencyScanners.Count, "security.dependencyScanners", "Duplicate scanner.");
        if (s.DependencyScanners.Contains(DependencyScanner.NpmAudit))
            Check(c.Modules.Any(m => m.Ecosystem is Ecosystem.Npm or Ecosystem.Pnpm or Ecosystem.Yarn), "security.dependencyScanners", "npm audit needs a Node module.");

        var envKeys = c.Environments.Select(e => e.Key).ToList();
        Check(c.Environments.Count <= 10, "environments", "At most 10 environments.");
        Check(envKeys.Distinct().Count() == envKeys.Count, "environments", "Environment keys must be unique.");
        for (var i = 0; i < c.Environments.Count; i++)
        {
            var e = c.Environments[i];
            var p = $"environments[{i}]";
            Check(EnvKeyRx().IsMatch(e.Key), $"{p}.key", "Lowercase letter first, then letters, digits or '-' (max 16).");
            Check(DisplayNameRx().IsMatch(e.DisplayName), $"{p}.displayName", "Letters, digits, spaces and . _ ( ) - only.");
            Check(IsBranchPattern(e.BranchPattern), $"{p}.branchPattern", "Invalid branch pattern.");
            if (e.Url is not null) Check(IsHttpUrl(e.Url), $"{p}.url", "Must be an http(s) URL without credentials.");
            ValidateDeploy(e.Deploy, $"{p}.deploy", Check);
            if (e.Deploy.Type != DeployTargetType.None)
                Check(c.Registry is not null && c.Modules.Any(m => m.Dockerfile is not null),
                    $"{p}.deploy", "Deploys ship container images: add a module with a Dockerfile and a registry.");
        }

        if (s.Dast.Enabled)
        {
            Check(s.Dast.Environments.Count > 0, "security.dast.environments", "Pick at least one environment to scan.");
            foreach (var key in s.Dast.Environments)
            {
                var env = c.Environments.FirstOrDefault(e => e.Key == key);
                Check(env is not null, "security.dast.environments", $"Unknown environment '{key}'.");
                Check(env?.Url is not null, "security.dast.environments", $"Environment '{key}' needs a URL for DAST.");
            }
            if (s.Dast.Mode == DastMode.Api)
                Check(s.Dast.ApiSpec is not null && (IsHttpUrl(s.Dast.ApiSpec) || IsRelativePath(s.Dast.ApiSpec)),
                    "security.dast.apiSpec", "API scans need an OpenAPI URL or repository path.");
        }

        Check(IsIdentifier(c.Compliance.ApproverGroup), "compliance.approverGroup", "Invalid Jenkins user or group.");

        var ai = c.AiReview;
        if (ai.Enabled)
        {
            Check(ModelRx().IsMatch(ai.Model), "aiReview.model", "Invalid model name.");
            Check(IsIdentifier(ai.CredentialId), "aiReview.credentialId", "Invalid credential ID.");
            if (ai.BaseUrl is not null) Check(IsHttpUrl(ai.BaseUrl) && ai.BaseUrl.StartsWith("https://"), "aiReview.baseUrl", "Must be an https URL.");
            Check(ai.Provider != AiProvider.AzureOpenAi || ai.BaseUrl is not null, "aiReview.baseUrl", "Azure OpenAI needs the resource URL.");
        }

        ValidateWorkflow(c, Check);

        Check(c.CustomStages.Count <= 20, "customStages", "At most 20 custom stages.");
        for (var i = 0; i < c.CustomStages.Count; i++)
        {
            var cs = c.CustomStages[i];
            Check(DisplayNameRx().IsMatch(cs.Name), $"customStages[{i}].name", "Letters, digits, spaces and . _ ( ) - only.");
            Check(cs.Steps.Length <= MaxCustomStageLength, $"customStages[{i}].steps", "Too long.");
            Check(BracesBalanced(cs.Steps), $"customStages[{i}].steps", "Unbalanced braces or quotes; the steps must be a self-contained block.");
        }

        Check(c.Options.TimeoutMinutes is >= 5 and <= 1440, "options.timeoutMinutes", "Between 5 and 1440.");
        Check(c.Options.BuildsToKeep is >= 1 and <= 1000, "options.buildsToKeep", "Between 1 and 1000.");

        return errors;
    }

    public static bool IsUrlPath(string? v) => v is not null && UrlPathRx().IsMatch(v) && !v.Split('/').Contains("..");

    private static void ValidateLogin(LoginConfig l, string p, Action<bool, string, string> check)
    {
        check(IsUrlPath(l.LoginPath), $"{p}.loginPath", "A path such as /login.");
        check(IsUrlPath(l.LogoutPath), $"{p}.logoutPath", "A path such as /logout.");
        check(FieldRx().IsMatch(l.UsernameField), $"{p}.usernameField", "Letters, digits, '_', '.' and '-' only.");
        check(FieldRx().IsMatch(l.PasswordField), $"{p}.passwordField", "Letters, digits, '_', '.' and '-' only.");
    }

    /// <summary>Environment keys referenced by scans must exist and have a URL.</summary>
    private static void CheckEnvironmentWithUrl(PipelineConfig c, string key, string path, Action<bool, string, string> check)
    {
        var env = c.Environments.FirstOrDefault(e => e.Key == key);
        check(env is not null, path, $"Unknown environment '{key}'.");
        check(env?.Url is not null, path, $"Environment '{key}' needs a URL.");
    }

    private static void ValidateWorkflow(PipelineConfig c, Action<bool, string, string> check)
    {
        var s = c.Security;
        var hasImages = c.Modules.Any(m => m.Dockerfile is not null);

        if (c.DependencyProxy is { } proxy)
        {
            check(IsHttpUrl(proxy.BaseUrl), "dependencyProxy.baseUrl", "Must be an http(s) URL without credentials.");
            check(IsIdentifier(proxy.CredentialId), "dependencyProxy.credentialId", "Invalid credential ID.");
            foreach (var (name, value) in new[] { ("mavenPath", proxy.MavenPath), ("npmPath", proxy.NpmPath), ("pypiPath", proxy.PypiPath), ("nugetPath", proxy.NugetPath), ("goPath", proxy.GoPath), ("composerPath", proxy.ComposerPath) })
                if (value is not null) check(IsRelativePath(value), $"dependencyProxy.{name}", "A repository path such as repository/maven-public.");
        }

        if (s.DependencyPinning)
            check(c.Modules.Any(m => m.Ecosystem == Ecosystem.Maven), "security.dependencyPinning", "Dependency pinning (Maven Enforcer) needs a Maven module.");
        var usesSnyk = s.DependencyScanners.Contains(DependencyScanner.Snyk) || (s.ContainerScan && s.ContainerScanner == ScannerTool.Snyk)
                       || (s.IacScan && s.IacScanner == ScannerTool.Snyk) || s.SnykMonitor;
        if (usesSnyk) check(IsIdentifier(s.SnykCredentialId), "security.snykCredentialId", "Invalid credential ID.");
        if (s.SnykMonitor) check(c.Environments.Any(e => e.Deploy.Type != DeployTargetType.None), "security.snykMonitor", "Snyk monitor runs on deploy: add a deploy environment.");

        var g = c.Signing;
        if (g.Enabled)
        {
            check(hasImages && c.Registry is not null, "signing", "Signing needs a module with a Dockerfile and a registry.");
            check(IsIdentifier(g.KeyCredentialId), "signing.keyCredentialId", "Invalid credential ID.");
            check(IsIdentifier(g.PasswordCredentialId), "signing.passwordCredentialId", "Invalid credential ID.");
            check(IsRelativePath(g.PublicKeyPath), "signing.publicKeyPath", "Must be a relative path inside the repository.");
        }

        var d = s.Dast;
        if (d.Ephemeral.Enabled)
        {
            var e = d.Ephemeral;
            check(hasImages, "security.dast.ephemeral", "The ephemeral scan needs a module with a Dockerfile.");
            if (e.Module is not null)
                check(c.Modules.Any(m => m.Name == e.Module && m.Dockerfile is not null), "security.dast.ephemeral.module", "Pick a module that builds an image.");
            check(e.ContainerPort is > 0 and < 65536, "security.dast.ephemeral.containerPort", "Between 1 and 65535.");
            check(IsUrlPath(e.HealthPath), "security.dast.ephemeral.healthPath", "A path such as /actuator/health.");
            if (e.ImagePullSecret is not null) check(IsIdentifier(e.ImagePullSecret), "security.dast.ephemeral.imagePullSecret", "Invalid secret name.");
            check(e.AppEnvironment.Count <= 30 && e.AppEnvironment.All(v => AppEnvRx().IsMatch(v)), "security.dast.ephemeral.appEnvironment", "KEY=value pairs; letters, digits and . _ : / , @ = - in values.");
            if (c.Agent.Type == AgentType.Kubernetes) check(c.Registry is not null, "security.dast.ephemeral", "On Kubernetes agents the scan image is pushed first: configure a registry.");
        }
        if (d.Authenticated.Enabled)
        {
            var a = d.Authenticated;
            CheckEnvironmentWithUrl(c, a.Environment, "security.dast.authenticated.environment", check);
            ValidateLogin(a.Login, "security.dast.authenticated.login", check);
            check(IsIdentifier(a.UserCredentialId), "security.dast.authenticated.userCredentialId", "Invalid credential ID.");
            check(IsIdentifier(a.AdminCredentialId), "security.dast.authenticated.adminCredentialId", "Invalid credential ID.");
            check(IsUrlPath(a.ProtectedPath), "security.dast.authenticated.protectedPath", "A path such as /account.");
            check(a.AdminOnlyPaths.Count is > 0 and <= 50, "security.dast.authenticated.adminOnlyPaths", "Between 1 and 50 admin-only paths.");
            check(a.AdminOnlyPaths.All(IsUrlPath), "security.dast.authenticated.adminOnlyPaths", "Paths such as /admin/users.");
            check(!a.BeforeProduction || c.Environments.Any(Compliance.ComplianceEnforcer.IsProduction), "security.dast.authenticated.beforeProduction", "No production environment found.");
        }
        if (d.Schedule.Enabled)
        {
            CheckEnvironmentWithUrl(c, d.Schedule.Environment, "security.dast.schedule.environment", check);
            check(CronRx().IsMatch(d.Schedule.BaselineCron) && d.Schedule.BaselineCron.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 5,
                "security.dast.schedule.baselineCron", "Five cron fields, e.g. H */6 * * *.");
            if (d.Schedule.AuthenticatedDaily) check(d.Authenticated.Enabled, "security.dast.schedule.authenticatedDaily", "Configure the authenticated scan first.");
        }

        var r = c.RateLimit;
        if (r.Enabled)
        {
            ValidateLogin(r.Login, "rateLimit.login", check);
            check(FieldRx().IsMatch(r.TestUsername), "rateLimit.testUsername", "Letters, digits, '_', '.' and '-' only.");
            check(r.Attempts is >= 3 and <= 200, "rateLimit.attempts", "Between 3 and 200.");
            check(r.ExpectedStatuses.Count > 0 && r.ExpectedStatuses.All(x => x is >= 400 and < 600), "rateLimit.expectedStatuses", "HTTP error statuses, e.g. 429, 423.");
            check(r.Environments.Count > 0, "rateLimit.environments", "Pick at least one environment.");
            foreach (var key in r.Environments) CheckEnvironmentWithUrl(c, key, "rateLimit.environments", check);
        }

        if (s.DockerfileLint)
        {
            check(hasImages, "security.dockerfileLint", "Dockerfile checks need a module with a Dockerfile.");
            check(s.AllowedBaseRegistries.Count <= 20 && s.AllowedBaseRegistries.All(IsHost), "security.allowedBaseRegistries", "Registry host names such as docker.io or mcr.microsoft.com.");
        }
        if (g.Attestations) check(g.Enabled, "signing.attestations", "Attestations are signed with the Cosign key: enable signing.");
        if (c.ClusterPolicy.Enabled)
        {
            check(c.Environments.Any(e => e.Deploy.Type == DeployTargetType.Helm), "clusterPolicy", "Cluster policies apply to Kubernetes (Helm) environments.");
            check(g.Enabled, "clusterPolicy", "The image policy verifies Cosign signatures: enable signing.");
        }

        for (var i = 0; i < c.Environments.Count; i++)
        {
            var env = c.Environments[i];
            if (env.PromoteFrom is not { } from) continue;
            var p = $"environments[{i}].promoteFrom";
            var source = c.Environments.FindIndex(x => x.Key == from);
            check(source >= 0, p, $"Unknown environment '{from}'.");
            check(source < i, p, "Promote from an environment listed before this one.");
            check(env.Deploy.Type != DeployTargetType.None && source >= 0 && c.Environments[source].Deploy.Type != DeployTargetType.None, p,
                "Both environments need a deploy target.");
            check(hasImages && c.Registry is not null, p, "Promotion moves container images: add a module with a Dockerfile and a registry.");
        }

        var t = c.Ticketing;
        if (t.Provider == TicketProvider.Jira)
        {
            check(IsIdentifier(t.JiraSite), "ticketing.jiraSite", "Name of the site in the Jira Steps plugin.");
            check(JiraKeyRx().IsMatch(t.ProjectKey), "ticketing.projectKey", "A Jira project key such as SEC.");
            check(IssueTypeRx().IsMatch(t.IssueType), "ticketing.issueType", "An issue type such as Bug.");
            check(t.MaxPerBuild is >= 1 and <= 100, "ticketing.maxPerBuild", "Between 1 and 100.");
            check(t.Sources.Count > 0, "ticketing.sources", "Pick at least one source.");
        }
    }

    private static void ValidateDeploy(DeployTarget d, string p, Action<bool, string, string> check)
    {
        switch (d.Type)
        {
            case DeployTargetType.Helm:
                check(IsIdentifier(d.KubeconfigCredentialId), $"{p}.kubeconfigCredentialId", "Invalid credential ID.");
                check(K8sNameRx().IsMatch(d.Namespace), $"{p}.namespace", "Invalid Kubernetes namespace.");
                check(K8sNameRx().IsMatch(d.ReleaseName), $"{p}.releaseName", "Invalid Helm release name.");
                check(IsRelativePath(d.ChartPath), $"{p}.chartPath", "Must be a relative path inside the repository.");
                break;
            case DeployTargetType.DockerSsh:
                check(IsIdentifier(d.SshCredentialId), $"{p}.sshCredentialId", "Invalid credential ID.");
                check(IsIdentifier(d.KnownHostsCredentialId), $"{p}.knownHostsCredentialId", "Invalid credential ID.");
                check(IsHost(d.Host), $"{p}.host", "Invalid host.");
                check(UnixUserRx().IsMatch(d.User), $"{p}.user", "Invalid user name.");
                check(IsAbsolutePath(d.RemoteDir), $"{p}.remoteDir", "Must be an absolute path.");
                break;
            case DeployTargetType.AzureAppService:
                check(IsIdentifier(d.AzureCredentialId), $"{p}.azureCredentialId", "Invalid credential ID.");
                check(AzureRgRx().IsMatch(d.ResourceGroup), $"{p}.resourceGroup", "Invalid resource group.");
                check(AzureAppRx().IsMatch(d.AppName), $"{p}.appName", "Invalid app name.");
                if (d.Slot is not null) check(AzureAppRx().IsMatch(d.Slot), $"{p}.slot", "Invalid slot name.");
                break;
        }
    }

    /// <summary>
    /// Checks that braces balance outside string literals and comments, so a custom
    /// block cannot close the surrounding stage and inject pipeline-level directives.
    /// </summary>
    public static bool BracesBalanced(string groovy)
    {
        var depth = 0;
        var i = 0;
        while (i < groovy.Length)
        {
            var c = groovy[i];
            if (c == '/' && i + 1 < groovy.Length && groovy[i + 1] == '/')
            {
                while (i < groovy.Length && groovy[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < groovy.Length && groovy[i + 1] == '*')
            {
                var end = groovy.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) return false;
                i = end + 2;
                continue;
            }
            if (c is '\'' or '"')
            {
                var triple = i + 2 < groovy.Length && groovy[i + 1] == c && groovy[i + 2] == c;
                var delim = triple ? new string(c, 3) : c.ToString();
                i += delim.Length;
                var closed = false;
                while (i < groovy.Length)
                {
                    if (groovy[i] == '\\') { i += 2; continue; }
                    if (string.CompareOrdinal(groovy, i, delim, 0, delim.Length) == 0) { i += delim.Length; closed = true; break; }
                    if (!triple && groovy[i] == '\n') return false;
                    i++;
                }
                if (!closed) return false;
                continue;
            }
            if (c == '{') depth++;
            else if (c == '}' && --depth < 0) return false;
            i++;
        }
        return depth == 0;
    }
}
