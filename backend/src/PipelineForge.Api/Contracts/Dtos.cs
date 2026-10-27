using System.Text.Json;
using PipelineForge.Core.Compliance;
using PipelineForge.Core.Generation;
using PipelineForge.Core.Importing;
using PipelineForge.Core.Model;
using PipelineForge.Core.Scanning;
using PipelineForge.Core.Validation;
using PipelineForge.Infrastructure.Persistence;

namespace PipelineForge.Api.Contracts;

public sealed record MeDto(Guid Id, string Email, string DisplayName, IReadOnlyList<WorkspaceDto> Workspaces);
public sealed record WorkspaceDto(Guid Id, string Name, bool AiEnabled, WorkspaceRole Role);
public sealed record CreateWorkspaceRequest(string Name);
public sealed record UpdateWorkspaceRequest(string? Name, bool? AiEnabled);
public sealed record MemberDto(Guid UserId, string Email, string DisplayName, WorkspaceRole Role);
public sealed record AddMemberRequest(string Email, WorkspaceRole Role);

public sealed record ConnectionDto(Guid Id, ConnectionKind Kind, string Name, string Provider, string BaseUrl, string? Username, string? Scope, string? Model, DateTimeOffset CreatedAt);
/// <summary>The secret is write-only: it goes straight to the secret store and is never returned.</summary>
public sealed record CreateConnectionRequest(ConnectionKind Kind, string Name, string Provider, string BaseUrl, string? Username, string? Scope, string? Model, string Secret);
public sealed record TestResult(bool Ok, string Message);

public sealed record ScanRequest(Guid ConnectionId, string Repository, string Branch);
public sealed record ScanResponse(RepoProfile Profile, PipelineConfig Config, IReadOnlyList<string> Reasons);

public sealed record ImportRequest(string? Content, Guid? ConnectionId, string? Repository, string? Branch, string? Path, string? Name);

public sealed record PresetDto(Guid Id, string Name, string Description, int Version, JsonElement Config, IReadOnlyList<string> LockedPaths, DateTimeOffset UpdatedAt);
public sealed record SavePresetRequest(string Name, string? Description, JsonElement Config, List<string>? LockedPaths);

public sealed record PreviewRequest(PipelineConfig Config, Guid? PresetId, bool ApplyPreset, List<string>? Files, string? BuiltInPresetId = null);
public sealed record GenerationResponse(
    bool Success,
    IReadOnlyList<ValidationError> Errors,
    IReadOnlyList<GeneratedFile> Files,
    ComplianceResult Compliance,
    IReadOnlyList<RequiredCredential> Credentials,
    IReadOnlyList<string> Plugins,
    PipelineConfig EffectiveConfig,
    IReadOnlyList<string> LockedPaths);

public sealed record ProjectSummaryDto(Guid Id, string Name, string Source, string? Repository, string? Branch, DateTimeOffset UpdatedAt);
public sealed record ProjectDto(Guid Id, string Name, string Source, Guid? ScmConnectionId, string? Repository, string? Branch,
    Guid? PresetId, int? PresetVersion, PipelineConfig Config, RepoProfile? Profile, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record SaveProjectRequest(string Name, string Source, PipelineConfig Config, Guid? ScmConnectionId, string? Repository,
    string? Branch, Guid? PresetId, RepoProfile? Profile);
public sealed record RunDto(Guid Id, DateTimeOffset CreatedAt, string ConfigSha256, string? PullRequestUrl, JsonElement Files);

public sealed record PullRequestCreate(string? TargetBranch, List<string>? Files);
public sealed record PullRequestResponse(string Url, string Branch, string? LibraryUrl = null, string? LibraryNote = null);
public sealed record ValidateRequest(Guid JenkinsConnectionId);
public sealed record JenkinsJobRequest(Guid JenkinsConnectionId, string JobName, string ScmCredentialId, bool CreateScheduledJob = false);
public sealed record JenkinsJobResponse(string Url, string? ScheduledUrl = null);
public sealed record AiReviewRequest(Guid AiConnectionId);

public sealed record AuditDto(long Id, DateTimeOffset At, Guid? UserId, string? UserEmail, string Action, string? Target, bool Success, string? Detail, string? IpAddress);

public sealed record MetaDto(
    IReadOnlyList<string> Ecosystems,
    IReadOnlyDictionary<string, string> DefaultVersions,
    IReadOnlyDictionary<string, string> ToolImages,
    IReadOnlyList<ComplianceControl> Iso27001Controls);

public sealed record BuiltInPresetDto(string Id, string Name, string Description, JsonElement Config);
public sealed record BranchProtectionRequest(List<string>? Branches, int Approvals = 1);
