using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PipelineForge.Api.Auth;
using PipelineForge.Api.Contracts;
using PipelineForge.Api.Services;
using PipelineForge.Infrastructure.Persistence;

namespace PipelineForge.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class WorkspacesController(AppDbContext db, ICurrentUser currentUser, Auditor audit, IConfiguration config) : ControllerBase
{
    [HttpGet("me")]
    public async Task<MeDto> Me(CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        var workspaces = await db.Memberships.Where(m => m.UserId == user.Id)
            .Select(m => new WorkspaceDto(m.WorkspaceId, m.Workspace!.Name, m.Workspace.AiEnabled, m.Role))
            .ToListAsync(ct);
        return new MeDto(user.Id, user.Email, user.DisplayName, workspaces.OrderBy(w => w.Name).ToList());
    }

    [HttpPost("workspaces")]
    public async Task<ActionResult<WorkspaceDto>> Create(CreateWorkspaceRequest request, CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        var creators = config.GetSection("Auth:WorkspaceCreators").Get<string[]>() ?? [];
        if (creators.Length > 0 && !creators.Contains(user.Email, StringComparer.OrdinalIgnoreCase)) return Forbid();
        var ws = new Workspace { Name = Require.Name(request.Name, "Name") };
        db.Workspaces.Add(ws);
        db.Memberships.Add(new Membership { WorkspaceId = ws.Id, UserId = user.Id, Role = WorkspaceRole.Admin });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("workspace.create", ws.Id, ws.Name, ct: ct);
        return new WorkspaceDto(ws.Id, ws.Name, ws.AiEnabled, WorkspaceRole.Admin);
    }

    [HttpPatch("workspaces/{workspaceId:guid}")]
    [Authorize(Policies.Admin)]
    public async Task<ActionResult<WorkspaceDto>> Update(Guid workspaceId, UpdateWorkspaceRequest request, CancellationToken ct)
    {
        var ws = await db.Workspaces.SingleAsync(w => w.Id == workspaceId, ct);
        if (request.Name is not null) ws.Name = Require.Name(request.Name, "Name");
        if (request.AiEnabled is { } ai) ws.AiEnabled = ai;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("workspace.update", ws.Id, ws.Name, detail: $"aiEnabled={ws.AiEnabled}", ct: ct);
        return new WorkspaceDto(ws.Id, ws.Name, ws.AiEnabled, WorkspaceRole.Admin);
    }

    [HttpGet("workspaces/{workspaceId:guid}/members")]
    [Authorize(Policies.Viewer)]
    public async Task<IReadOnlyList<MemberDto>> Members(Guid workspaceId, CancellationToken ct) =>
        await db.Memberships.Where(m => m.WorkspaceId == workspaceId)
            .Select(m => new MemberDto(m.UserId, m.User!.Email, m.User.DisplayName, m.Role))
            .ToListAsync(ct);

    /// <summary>Adds or changes a member. The person must have signed in once so their account exists.</summary>
    [HttpPut("workspaces/{workspaceId:guid}/members")]
    [Authorize(Policies.Admin)]
    public async Task<ActionResult<MemberDto>> SetMember(Guid workspaceId, AddMemberRequest request, CancellationToken ct)
    {
        var email = Require.Name(request.Email, "Email", 320);
        var target = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (target is null) return NotFound(new ProblemDetails { Title = "No user with that email has signed in yet." });
        var me = await currentUser.GetAsync(ct);
        if (target.Id == me.Id) return BadRequest(new ProblemDetails { Title = "You cannot change your own role." });
        var membership = await db.Memberships.SingleOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == target.Id, ct);
        if (membership is null) db.Memberships.Add(membership = new Membership { WorkspaceId = workspaceId, UserId = target.Id });
        membership.Role = request.Role;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("member.set", workspaceId, target.Email, detail: request.Role.ToString(), ct: ct);
        return new MemberDto(target.Id, target.Email, target.DisplayName, request.Role);
    }

    [HttpDelete("workspaces/{workspaceId:guid}/members/{userId:guid}")]
    [Authorize(Policies.Admin)]
    public async Task<IActionResult> RemoveMember(Guid workspaceId, Guid userId, CancellationToken ct)
    {
        var me = await currentUser.GetAsync(ct);
        if (userId == me.Id) return BadRequest(new ProblemDetails { Title = "You cannot remove yourself." });
        var membership = await db.Memberships.SingleOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, ct);
        if (membership is null) return NotFound();
        db.Memberships.Remove(membership);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("member.remove", workspaceId, userId.ToString(), ct: ct);
        return NoContent();
    }

    [HttpGet("workspaces/{workspaceId:guid}/audit")]
    [Authorize(Policies.Admin)]
    public async Task<IReadOnlyList<AuditDto>> Audit(Guid workspaceId, [FromQuery] long? before, [FromQuery] int take = 100, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 500);
        var q = db.AuditEvents.Where(a => a.WorkspaceId == workspaceId);
        if (before is { } b) q = q.Where(a => a.Id < b);
        var rows = await q.OrderByDescending(a => a.Id).Take(take).ToListAsync(ct);
        var ids = rows.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).Distinct().ToList();
        var emails = await db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
        return rows.Select(r => new AuditDto(r.Id, r.At, r.UserId, r.UserId is { } id ? emails.GetValueOrDefault(id) : null,
            r.Action, r.Target, r.Success, r.Detail, r.IpAddress)).ToList();
    }
}
