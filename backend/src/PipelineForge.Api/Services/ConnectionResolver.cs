using Microsoft.EntityFrameworkCore;
using PipelineForge.Core.Ai;
using PipelineForge.Core.Model;
using PipelineForge.Core.Scm;
using PipelineForge.Infrastructure.Jenkins;
using PipelineForge.Infrastructure.Persistence;
using PipelineForge.Infrastructure.Secrets;

namespace PipelineForge.Api.Services;

public sealed class NotFoundException(string message) : Exception(message);

public sealed class ForbiddenException(string message) : Exception(message);

/// <summary>Loads a connection of the given workspace and resolves its secret just in time.</summary>
public sealed class ConnectionResolver(AppDbContext db, ISecretStore secrets)
{
    private async Task<Connection> Load(Guid workspaceId, Guid id, ConnectionKind kind, CancellationToken ct) =>
        await db.Connections.SingleOrDefaultAsync(c => c.Id == id && c.WorkspaceId == workspaceId && c.Kind == kind, ct)
        ?? throw new NotFoundException("Connection not found.");

    public async Task<(Connection Connection, ScmConnectionInfo Info)> ScmAsync(Guid workspaceId, Guid id, CancellationToken ct)
    {
        var c = await Load(workspaceId, id, ConnectionKind.Scm, ct);
        var token = await secrets.GetAsync(c.SecretRef, ct);
        return (c, new ScmConnectionInfo(Enum.Parse<ScmKind>(c.Provider), new Uri(c.BaseUrl), c.Username, token, c.Scope));
    }

    public async Task<JenkinsConnectionInfo> JenkinsAsync(Guid workspaceId, Guid id, CancellationToken ct)
    {
        var c = await Load(workspaceId, id, ConnectionKind.Jenkins, ct);
        return new JenkinsConnectionInfo(new Uri(c.BaseUrl), c.Username ?? "", await secrets.GetAsync(c.SecretRef, ct));
    }

    public async Task<AiConnectionInfo> AiAsync(Guid workspaceId, Guid id, CancellationToken ct)
    {
        var c = await Load(workspaceId, id, ConnectionKind.Ai, ct);
        var provider = Enum.Parse<AiProvider>(c.Provider);
        Uri? baseUrl = string.IsNullOrEmpty(c.BaseUrl) ? null : new Uri(c.BaseUrl);
        return new AiConnectionInfo(provider, c.Model ?? "claude-opus-5-5", baseUrl, await secrets.GetAsync(c.SecretRef, ct));
    }
}
