using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using PipelineForge.Infrastructure.Persistence;

namespace PipelineForge.Api.Auth;

public sealed record WorkspaceRoleRequirement(WorkspaceRole Minimum) : IAuthorizationRequirement;

public static class Policies
{
    public const string Viewer = "workspace:viewer";
    public const string Maintainer = "workspace:maintainer";
    public const string Admin = "workspace:admin";

    public static void Add(AuthorizationOptions o)
    {
        o.AddPolicy(Viewer, p => p.RequireAuthenticatedUser().AddRequirements(new WorkspaceRoleRequirement(WorkspaceRole.Viewer)));
        o.AddPolicy(Maintainer, p => p.RequireAuthenticatedUser().AddRequirements(new WorkspaceRoleRequirement(WorkspaceRole.Maintainer)));
        o.AddPolicy(Admin, p => p.RequireAuthenticatedUser().AddRequirements(new WorkspaceRoleRequirement(WorkspaceRole.Admin)));
        o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    }
}

/// <summary>
/// Checks membership of the workspace in the route (<c>{workspaceId}</c>). Every query in the
/// controllers is additionally filtered by that workspace id, so a valid membership in one
/// workspace never exposes another workspace's rows.
/// </summary>
public sealed class WorkspaceRoleHandler(IHttpContextAccessor http, ICurrentUser currentUser, AppDbContext db)
    : AuthorizationHandler<WorkspaceRoleRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, WorkspaceRoleRequirement requirement)
    {
        var routeValue = http.HttpContext?.GetRouteValue("workspaceId")?.ToString();
        if (!Guid.TryParse(routeValue, out var workspaceId)) return;
        var user = await currentUser.GetAsync();
        var role = await db.Memberships
            .Where(m => m.WorkspaceId == workspaceId && m.UserId == user.Id)
            .Select(m => (WorkspaceRole?)m.Role)
            .SingleOrDefaultAsync();
        if (role is not null && role >= requirement.Minimum) context.Succeed(requirement);
    }
}
