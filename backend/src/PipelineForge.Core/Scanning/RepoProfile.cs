using PipelineForge.Core.Model;

namespace PipelineForge.Core.Scanning;

/// <summary>Read-only access to a repository snapshot. Implementations never execute repository code.</summary>
public interface IRepoContentReader
{
    Task<IReadOnlyList<string>> ListFilesAsync(CancellationToken ct);
    /// <summary>Returns null when the file is missing or larger than the reader's limit.</summary>
    Task<string?> ReadFileAsync(string path, CancellationToken ct);
}

public sealed class DetectedModule
{
    public required string Name { get; set; }
    public required string Path { get; set; }
    public required Ecosystem Ecosystem { get; set; }
    public string? LanguageVersion { get; set; }
    public string? Dockerfile { get; set; }
    public List<string> Evidence { get; set; } = [];
}

public sealed class RepoProfile
{
    public List<DetectedModule> Modules { get; set; } = [];
    public List<string> HelmCharts { get; set; } = [];
    public bool HasKubernetesManifests { get; set; }
    public bool HasTerraform { get; set; }
    public bool HasDockerCompose { get; set; }
    public string? ExistingJenkinsfile { get; set; }
    public string? SonarProjectKey { get; set; }
    public string? SonarOrganization { get; set; }
    public int FileCount { get; set; }
    public bool Truncated { get; set; }
    public List<string> Notes { get; set; } = [];
}
