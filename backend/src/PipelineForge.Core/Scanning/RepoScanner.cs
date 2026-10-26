using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using PipelineForge.Core.Model;

namespace PipelineForge.Core.Scanning;

/// <summary>
/// Detects modules, languages and deploy hints from a file listing plus a bounded number
/// of manifest reads. Parsers are hardened: no DTDs, no external entities, depth limits.
/// </summary>
public static partial class RepoScanner
{
    public const int MaxFiles = 50_000;
    public const int MaxReads = 200;

    private static readonly string[] IgnoredDirs =
        ["node_modules", "vendor", ".git", "target", "build", "dist", "bin", "obj", ".venv", "venv", "__pycache__", ".gradle", ".idea", ".vscode"];

    [GeneratedRegex(@"JavaLanguageVersion\.of\(\s*(\d+)\s*\)")] private static partial Regex GradleToolchainRx();
    [GeneratedRegex(@"(?:sourceCompatibility|targetCompatibility)\s*=\s*(?:JavaVersion\.VERSION_)?['""]?(\d+)")] private static partial Regex GradleCompatRx();
    [GeneratedRegex(@"<TargetFrameworks?>\s*net(\d+\.\d+)")] private static partial Regex TargetFrameworkRx();
    [GeneratedRegex(@"^go\s+(\d+\.\d+)", RegexOptions.Multiline)] private static partial Regex GoVersionRx();
    [GeneratedRegex(@"(\d+)(?:\.(\d+))?")] private static partial Regex VersionNumberRx();
    [GeneratedRegex(@"requires-python\s*=\s*['""][^'""\d]*(\d+\.\d+)")] private static partial Regex RequiresPythonRx();
    [GeneratedRegex(@"[^a-z0-9._-]+")] private static partial Regex NameCleanRx();

    public static async Task<RepoProfile> ScanAsync(IRepoContentReader reader, string repoName, CancellationToken ct = default)
    {
        var all = await reader.ListFilesAsync(ct);
        var profile = new RepoProfile { FileCount = all.Count, Truncated = all.Count > MaxFiles };
        var files = all.Take(MaxFiles)
            .Select(f => f.Replace('\\', '/').TrimStart('/'))
            .Where(f => !f.Split('/').SkipLast(1).Any(seg => IgnoredDirs.Contains(seg)))
            .ToList();
        var fileSet = files.ToHashSet(StringComparer.Ordinal);
        var reads = 0;

        async Task<string?> Read(string path)
        {
            if (reads >= MaxReads) return null;
            reads++;
            return await reader.ReadFileAsync(path, ct);
        }

        static string Dir(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : ".";
        static string FileName(string path) => path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path;

        // Top-most directory wins: nested manifests belong to the parent module (Maven reactor, workspaces).
        List<string> TopMost(IEnumerable<string> dirs)
        {
            var sorted = dirs.Distinct().OrderBy(d => d == "." ? 0 : d.Count(ch => ch == '/') + 1).ToList();
            var result = new List<string>();
            foreach (var d in sorted)
                if (!result.Any(r => r == "." || d == r || d.StartsWith(r + "/", StringComparison.Ordinal)))
                    result.Add(d);
            return result;
        }

        var claimed = new List<string>();
        void AddModule(string dir, Ecosystem eco, string? version, string evidence)
        {
            var name = dir == "." ? repoName : FileName(dir);
            name = NameCleanRx().Replace(name.ToLowerInvariant(), "-").Trim('-', '.', '_');
            if (name.Length == 0) name = "app";
            if (name.Length > 63) name = name[..63];
            var unique = name;
            for (var i = 2; profile.Modules.Any(m => m.Name == unique); i++) unique = $"{name}-{i}";
            profile.Modules.Add(new DetectedModule { Name = unique, Path = dir, Ecosystem = eco, LanguageVersion = version, Evidence = [evidence] });
            claimed.Add(dir);
        }

        bool Claimed(string dir) => claimed.Any(c => c == dir);

        // Maven
        foreach (var dir in TopMost(files.Where(f => FileName(f) == "pom.xml").Select(Dir)))
        {
            var pom = await Read(dir == "." ? "pom.xml" : $"{dir}/pom.xml");
            AddModule(dir, Ecosystem.Maven, pom is null ? null : MavenJavaVersion(pom), "pom.xml");
        }

        // Gradle
        var gradleFiles = new[] { "build.gradle", "build.gradle.kts", "settings.gradle", "settings.gradle.kts" };
        foreach (var dir in TopMost(files.Where(f => gradleFiles.Contains(FileName(f))).Select(Dir)))
        {
            if (Claimed(dir)) continue;
            string? version = null;
            foreach (var candidate in new[] { "build.gradle.kts", "build.gradle" })
            {
                var path = dir == "." ? candidate : $"{dir}/{candidate}";
                if (!fileSet.Contains(path)) continue;
                var text = await Read(path);
                if (text is null) continue;
                var m = GradleToolchainRx().Match(text);
                if (!m.Success) m = GradleCompatRx().Match(text);
                if (m.Success) version = m.Groups[1].Value;
                break;
            }
            AddModule(dir, Ecosystem.Gradle, version, "Gradle build");
        }

        // .NET
        var netDirs = TopMost(files.Where(f => f.EndsWith(".sln") || f.EndsWith(".slnx") || f.EndsWith(".csproj") || f.EndsWith(".fsproj")).Select(Dir));
        foreach (var dir in netDirs)
        {
            if (Claimed(dir)) continue;
            string? version = null;
            var project = files.FirstOrDefault(f => (f.EndsWith(".csproj") || f.EndsWith(".fsproj")) && (dir == "." || f.StartsWith(dir + "/")));
            if (project is not null && await Read(project) is { } xml)
            {
                var m = TargetFrameworkRx().Match(xml);
                if (m.Success) version = m.Groups[1].Value;
            }
            AddModule(dir, Ecosystem.DotNet, version, project ?? "solution");
        }

        // Node
        foreach (var dir in TopMost(files.Where(f => FileName(f) == "package.json").Select(Dir)))
        {
            if (Claimed(dir)) continue;
            string P(string f) => dir == "." ? f : $"{dir}/{f}";
            var eco = fileSet.Contains(P("pnpm-lock.yaml")) ? Ecosystem.Pnpm
                : fileSet.Contains(P("yarn.lock")) ? Ecosystem.Yarn
                : Ecosystem.Npm;
            string? version = null;
            if (fileSet.Contains(P(".nvmrc")) && await Read(P(".nvmrc")) is { } nvmrc)
                version = MajorVersion(nvmrc);
            if (version is null && await Read(P("package.json")) is { } pkg)
                version = NodeEngine(pkg);
            AddModule(dir, eco, version, "package.json");
        }

        // Python
        var pyMarkers = new[] { "pyproject.toml", "requirements.txt", "setup.py", "Pipfile" };
        foreach (var dir in TopMost(files.Where(f => pyMarkers.Contains(FileName(f))).Select(Dir)))
        {
            if (Claimed(dir)) continue;
            string P(string f) => dir == "." ? f : $"{dir}/{f}";
            string? version = null;
            if (fileSet.Contains(P(".python-version")) && await Read(P(".python-version")) is { } pv)
                version = MinorVersion(pv);
            if (version is null && fileSet.Contains(P("pyproject.toml")) && await Read(P("pyproject.toml")) is { } pyproject)
            {
                var m = RequiresPythonRx().Match(pyproject);
                if (m.Success) version = m.Groups[1].Value;
            }
            AddModule(dir, Ecosystem.Python, version, "Python project");
        }

        // Go
        foreach (var dir in TopMost(files.Where(f => FileName(f) == "go.mod").Select(Dir)))
        {
            if (Claimed(dir)) continue;
            var gomod = await Read(dir == "." ? "go.mod" : $"{dir}/go.mod");
            var m = gomod is null ? Match.Empty : GoVersionRx().Match(gomod);
            AddModule(dir, Ecosystem.Go, m.Success ? m.Groups[1].Value : null, "go.mod");
        }

        // PHP
        foreach (var dir in TopMost(files.Where(f => FileName(f) == "composer.json").Select(Dir)))
        {
            if (Claimed(dir)) continue;
            var composer = await Read(dir == "." ? "composer.json" : $"{dir}/composer.json");
            AddModule(dir, Ecosystem.Composer, composer is null ? null : ComposerPhp(composer), "composer.json");
        }

        // Dockerfiles belong to the closest module at or above them.
        var dockerfiles = files.Where(f => FileName(f) is "Dockerfile" || FileName(f).EndsWith(".Dockerfile") || FileName(f).StartsWith("Dockerfile.")).ToList();
        foreach (var df in dockerfiles.OrderBy(f => f.Length))
        {
            var dir = Dir(df);
            var owner = profile.Modules
                .Where(m => m.Dockerfile is null && (m.Path == dir || m.Path == "." || dir.StartsWith(m.Path + "/")))
                .OrderByDescending(m => m.Path.Length)
                .FirstOrDefault();
            if (owner is null && profile.Modules.Count == 1 && profile.Modules[0].Dockerfile is null) owner = profile.Modules[0];
            if (owner is not null && (owner.Path == dir || FileName(df) == "Dockerfile"))
            {
                owner.Dockerfile = df;
                owner.Evidence.Add(df);
            }
        }

        profile.HelmCharts = files.Where(f => FileName(f) == "Chart.yaml").Select(Dir).ToList();
        profile.HasKubernetesManifests = files.Any(f =>
            (f.EndsWith(".yaml") || f.EndsWith(".yml")) &&
            f.Split('/').Any(seg => seg is "k8s" or "kubernetes" or "manifests" or "kustomize") );
        profile.HasTerraform = files.Any(f => f.EndsWith(".tf"));
        profile.HasDockerCompose = files.Any(f => FileName(f) is "docker-compose.yml" or "docker-compose.yaml" or "compose.yml" or "compose.yaml");
        profile.ExistingJenkinsfile = files.FirstOrDefault(f => f == "Jenkinsfile") ?? files.FirstOrDefault(f => FileName(f) == "Jenkinsfile");
        if (fileSet.Contains("sonar-project.properties") && await Read("sonar-project.properties") is { } sonar)
        {
            foreach (var line in sonar.Split('\n'))
            {
                var l = line.Trim();
                if (l.StartsWith("sonar.projectKey=")) profile.SonarProjectKey = l["sonar.projectKey=".Length..].Trim();
                if (l.StartsWith("sonar.organization=")) profile.SonarOrganization = l["sonar.organization=".Length..].Trim();
            }
        }

        if (profile.Modules.Count == 0) profile.Notes.Add("No supported build files found. Add modules manually.");
        if (profile.Truncated) profile.Notes.Add($"Large repository: only the first {MaxFiles} files were scanned.");
        if (reads >= MaxReads) profile.Notes.Add("Read limit reached; some versions may be defaults.");
        return profile;
    }

    /// <summary>Java version from a POM, parsed without DTDs or external entities.</summary>
    public static string? MavenJavaVersion(string pom)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 2_000_000,
            IgnoreComments = true,
        };
        try
        {
            using var reader = XmlReader.Create(new StringReader(pom), settings);
            var doc = new XmlDocument { XmlResolver = null };
            doc.Load(reader);
            foreach (var name in new[] { "maven.compiler.release", "java.version", "maven.compiler.source", "maven.compiler.target" })
            {
                var node = doc.GetElementsByTagName(name);
                if (node.Count > 0 && MajorVersion(node[0]!.InnerText) is { } v) return v == "1" ? MinorAsJava(node[0]!.InnerText) : v;
            }
            var release = doc.GetElementsByTagName("release");
            if (release.Count > 0) return MajorVersion(release[0]!.InnerText);
        }
        catch (XmlException)
        {
        }
        return null;
    }

    private static string? MinorAsJava(string text)
    {
        // "1.8" style versions.
        var m = VersionNumberRx().Match(text);
        return m.Success && m.Groups[2].Success ? m.Groups[2].Value : null;
    }

    private static string? MajorVersion(string text)
    {
        var m = VersionNumberRx().Match(text);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string? MinorVersion(string text)
    {
        var m = VersionNumberRx().Match(text);
        return m.Success && m.Groups[2].Success ? $"{m.Groups[1].Value}.{m.Groups[2].Value}" : null;
    }

    private static JsonDocument? ParseJson(string json)
    {
        try
        {
            return JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32, CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? NodeEngine(string packageJson)
    {
        using var doc = ParseJson(packageJson);
        if (doc?.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("engines", out var engines)
            && engines.ValueKind == JsonValueKind.Object
            && engines.TryGetProperty("node", out var node)
            && node.ValueKind == JsonValueKind.String)
            return MajorVersion(node.GetString()!);
        return null;
    }

    public static string? ComposerPhp(string composerJson)
    {
        using var doc = ParseJson(composerJson);
        if (doc?.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("require", out var req)
            && req.ValueKind == JsonValueKind.Object
            && req.TryGetProperty("php", out var php)
            && php.ValueKind == JsonValueKind.String)
            return MinorVersion(php.GetString()!);
        return null;
    }
}
