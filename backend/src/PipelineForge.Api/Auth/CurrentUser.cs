using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PipelineForge.Infrastructure.Persistence;

namespace PipelineForge.Api.Auth;

public interface ICurrentUser
{
    Task<User> GetAsync(CancellationToken ct = default);
    string? IpAddress { get; }
}

/// <summary>Maps the OIDC principal (issuer + subject) to a local user, creating it on first sight.</summary>
public sealed class CurrentUser(IHttpContextAccessor http, AppDbContext db) : ICurrentUser
{
    private User? _cached;

    public string? IpAddress => http.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public async Task<User> GetAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;
        var principal = http.HttpContext?.User ?? throw new UnauthorizedAccessException();
        var sub = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? throw new UnauthorizedAccessException("Token has no subject.");
        var iss = principal.FindFirstValue("iss") ?? "";
        var subject = $"{iss}|{sub}";
        var email = principal.FindFirstValue("email") ?? principal.FindFirstValue(ClaimTypes.Email) ?? "";
        var name = principal.FindFirstValue("name") ?? principal.FindFirstValue("preferred_username") ?? email;

        var user = await db.Users.SingleOrDefaultAsync(u => u.Subject == subject, ct);
        if (user is null)
        {
            user = new User { Subject = subject, Email = email, DisplayName = name };
            db.Users.Add(user);
        }
        else
        {
            user.Email = email;
            user.DisplayName = name;
            user.LastSeenAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return _cached = user;
    }
}
