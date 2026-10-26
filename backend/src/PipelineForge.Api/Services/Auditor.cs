using PipelineForge.Api.Auth;
using PipelineForge.Infrastructure.Audit;

namespace PipelineForge.Api.Services;

/// <summary>Audit helper that fills in the current user and client address.</summary>
public sealed class Auditor(IAuditLog log, ICurrentUser user)
{
    public async Task WriteAsync(string action, Guid? workspaceId, string? target, bool success = true, string? detail = null, CancellationToken ct = default)
    {
        var u = await user.GetAsync(ct);
        await log.WriteAsync(new AuditEntry(action, workspaceId, u.Id, target, success, detail, user.IpAddress), ct);
    }
}
