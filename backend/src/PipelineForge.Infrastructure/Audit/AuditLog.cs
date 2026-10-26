using PipelineForge.Infrastructure.Persistence;

namespace PipelineForge.Infrastructure.Audit;

public sealed record AuditEntry(string Action, Guid? WorkspaceId, Guid? UserId, string? Target, bool Success, string? Detail, string? IpAddress);

public interface IAuditLog
{
    Task WriteAsync(AuditEntry entry, CancellationToken ct = default);
}

/// <summary>Writes to the append-only audit table. Details are truncated and never contain secrets.</summary>
public sealed class AuditLog(AppDbContext db) : IAuditLog
{
    public async Task WriteAsync(AuditEntry e, CancellationToken ct = default)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Action = Trim(e.Action, 100)!,
            WorkspaceId = e.WorkspaceId,
            UserId = e.UserId,
            Target = Trim(e.Target, 500),
            Success = e.Success,
            Detail = Trim(e.Detail, 2000),
            IpAddress = Trim(e.IpAddress, 64),
        });
        await db.SaveChangesAsync(ct);
    }

    private static string? Trim(string? v, int max) => v is null || v.Length <= max ? v : v[..max];
}
