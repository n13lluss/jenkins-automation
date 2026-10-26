namespace PipelineForge.Core.Scm;

public enum ScmKind { BitbucketCloud, BitbucketServer, GitHub }

public sealed record ScmRepository(string FullName, string Name, string? DefaultBranch, string? WebUrl, bool IsPrivate);

public sealed record ScmBranch(string Name, string? CommitId);

public sealed record FileChange(string Path, string Content);

public sealed record PullRequestRequest(string Repository, string SourceBranch, string TargetBranch, string Title, string Description);

public sealed record PullRequestResult(string Url, string Id);

/// <summary>
/// The connection details an SCM provider needs. The token is resolved from the secret
/// store just before use and is never persisted with this object.
/// </summary>
public sealed record ScmConnectionInfo(ScmKind Kind, Uri BaseUrl, string? Username, string Token, string? Workspace);

/// <summary>Repository access. Read operations never clone or execute repository code.</summary>
public interface ISourceControlProvider
{
    ScmKind Kind { get; }
    Task<IReadOnlyList<ScmRepository>> ListRepositoriesAsync(ScmConnectionInfo conn, string? query, CancellationToken ct);
    Task<IReadOnlyList<ScmBranch>> ListBranchesAsync(ScmConnectionInfo conn, string repository, CancellationToken ct);
    Task<IReadOnlyList<string>> ListFilesAsync(ScmConnectionInfo conn, string repository, string branch, CancellationToken ct);
    /// <summary>Returns null when missing or larger than <paramref name="maxBytes"/>.</summary>
    Task<string?> GetFileAsync(ScmConnectionInfo conn, string repository, string branch, string path, int maxBytes, CancellationToken ct);
    Task CreateBranchAsync(ScmConnectionInfo conn, string repository, string newBranch, string fromBranch, CancellationToken ct);
    Task CommitFilesAsync(ScmConnectionInfo conn, string repository, string branch, string message, IReadOnlyList<FileChange> files, CancellationToken ct);
    Task<PullRequestResult> CreatePullRequestAsync(ScmConnectionInfo conn, PullRequestRequest request, CancellationToken ct);
    /// <summary>Cheap authenticated call used by "Test connection".</summary>
    Task<string> WhoAmIAsync(ScmConnectionInfo conn, CancellationToken ct);
}

/// <summary>Adapts a provider + repository + branch to the scanner's reader interface.</summary>
public sealed class ScmRepoReader(ISourceControlProvider provider, ScmConnectionInfo conn, string repository, string branch, int maxFileBytes = 1_000_000)
    : Scanning.IRepoContentReader
{
    public Task<IReadOnlyList<string>> ListFilesAsync(CancellationToken ct) => provider.ListFilesAsync(conn, repository, branch, ct);
    public Task<string?> ReadFileAsync(string path, CancellationToken ct) => provider.GetFileAsync(conn, repository, branch, path, maxFileBytes, ct);
}
