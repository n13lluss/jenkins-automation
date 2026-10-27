using System.Reflection;
using System.Text.RegularExpressions;

namespace PipelineForge.Core.Drift;

/// <summary>
/// Every generated text file carries "pipelineforge-version: x" in a comment on its first line (after a shebang
/// or XML declaration), so a repository copy can be recognised as generated and compared with a newer version.
/// </summary>
public static partial class VersionMarker
{
    public const string Key = "pipelineforge-version:";

    public static readonly string Version =
        (typeof(VersionMarker).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    [GeneratedRegex(@"pipelineforge-version: ([0-9A-Za-z.\-]{1,40})")] private static partial Regex MarkerRx();

    /// <summary>Comment syntax per file type, or null for formats without comments (JSON, TSV).</summary>
    private static (string Open, string Close)? Comment(string path)
    {
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (name.StartsWith("Jenkinsfile", StringComparison.Ordinal) || ext is ".groovy" or ".gradle") return ("// ", "");
        if (ext is ".yaml" or ".yml" or ".py" or ".toml" or ".properties" or ".sh" || name is ".trivyignore" or ".gitleaksignore") return ("# ", "");
        if (ext is ".xml" or ".md") return ("<!-- ", " -->");
        return null;
    }

    public static string Apply(string path, string content)
    {
        if (Comment(path) is not var (open, close)) return content;
        var line = $"{open}{Key} {Version}{close}\n";
        // Keep a shebang or XML declaration first.
        if (content.StartsWith("#!", StringComparison.Ordinal) || content.StartsWith("<?xml", StringComparison.Ordinal))
        {
            var end = content.IndexOf('\n');
            return end < 0 ? content + "\n" + line : content[..(end + 1)] + line + content[(end + 1)..];
        }
        return line + content;
    }

    public static string? Read(string content)
    {
        foreach (var l in content.Split('\n').Take(3))
            if (MarkerRx().Match(l) is { Success: true } m) return m.Groups[1].Value;
        return null;
    }

    /// <summary>The content without the marker line and with normalised line endings, for comparison.</summary>
    public static string Normalise(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n').ToList();
        var i = lines.FindIndex(0, Math.Min(3, lines.Count), l => l.Contains(Key, StringComparison.Ordinal));
        if (i >= 0) lines.RemoveAt(i);
        return string.Join('\n', lines).TrimEnd('\n') + "\n";
    }
}
