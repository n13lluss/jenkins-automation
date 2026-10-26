namespace PipelineForge.Infrastructure.Persistence;

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>OIDC issuer + subject; the stable identity key.</summary>
    public required string Subject { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum WorkspaceRole { Viewer = 0, Maintainer = 1, Admin = 2 }

public sealed class Workspace
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    /// <summary>AI calls are opt-in per workspace.</summary>
    public bool AiEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Membership
{
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public WorkspaceRole Role { get; set; }
    public Workspace? Workspace { get; set; }
    public User? User { get; set; }
}

public enum ConnectionKind { Scm, Jenkins, Ai }

/// <summary>An external system. Only a reference to the secret is stored, never the secret.</summary>
public sealed class Connection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public ConnectionKind Kind { get; set; }
    public required string Name { get; set; }
    /// <summary>ScmKind, "jenkins" or AiProvider name.</summary>
    public required string Provider { get; set; }
    public required string BaseUrl { get; set; }
    public string? Username { get; set; }
    /// <summary>Bitbucket workspace / GitHub organisation.</summary>
    public string? Scope { get; set; }
    public string? Model { get; set; }
    public required string SecretRef { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CreatedBy { get; set; }
}

public sealed class Preset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public int LatestVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<PresetVersion> Versions { get; set; } = [];
}

public sealed class PresetVersion
{
    public Guid PresetId { get; set; }
    public int Version { get; set; }
    public required string ConfigJson { get; set; }
    public string LockedPathsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CreatedBy { get; set; }
}

public sealed class PipelineProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public required string Name { get; set; }
    /// <summary>scan, blank or import.</summary>
    public required string Source { get; set; }
    public Guid? ScmConnectionId { get; set; }
    public string? Repository { get; set; }
    public string? Branch { get; set; }
    public Guid? PresetId { get; set; }
    public int? PresetVersion { get; set; }
    public required string ConfigJson { get; set; }
    public string? ProfileJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CreatedBy { get; set; }
}

public sealed class GenerationRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid WorkspaceId { get; set; }
    /// <summary>File paths with SHA-256 hashes, as JSON.</summary>
    public required string FilesJson { get; set; }
    public required string ConfigSha256 { get; set; }
    public string? PullRequestUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CreatedBy { get; set; }
}

/// <summary>Append-only: the DbContext refuses updates and deletes.</summary>
public sealed class AuditEvent
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public Guid? WorkspaceId { get; set; }
    public Guid? UserId { get; set; }
    public required string Action { get; set; }
    public string? Target { get; set; }
    public bool Success { get; set; } = true;
    public string? Detail { get; set; }
    public string? IpAddress { get; set; }
}
