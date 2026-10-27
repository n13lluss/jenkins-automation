using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PipelineForge.Api.Auth;
using PipelineForge.Api.Services;
using PipelineForge.Core.Metrics;
using PipelineForge.Infrastructure.Audit;
using PipelineForge.Infrastructure.Persistence;

namespace PipelineForge.Api.Controllers;

public sealed record DeploymentReport(
    string Environment, string Result, DateTimeOffset StartedAt, DateTimeOffset FinishedAt,
    string? Digest, string? Commit, DateTimeOffset? CommitTime, string? BuildUrl);

public sealed record IngestTokenDto(Guid Id, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt);
public sealed record IngestTokenCreated(Guid Id, string Prefix, string Token);
public sealed record MetricsResponse(int Days, IReadOnlyList<EnvironmentMetrics> Environments);

/// <summary>Deploy reports from Jenkins. Authenticated by a per-pipeline ingest token, not a user session.</summary>
[ApiController]
[Route("api/ingest")]
public sealed partial class IngestController(AppDbContext db, IAuditLog audit) : ControllerBase
{
    public const string TokenPrefix = "pf_ing_";
    private const int MaxBodyBytes = 16 * 1024;

    [GeneratedRegex(@"^[a-z][a-z0-9-]{0,15}$")] private static partial Regex EnvRx();
    [GeneratedRegex(@"^sha256:[a-f0-9]{64}$")] private static partial Regex DigestRx();
    [GeneratedRegex(@"^[a-f0-9]{7,64}$")] private static partial Regex CommitRx();
    [GeneratedRegex(@"^pf_ing_[A-Za-z0-9_-]{43}$")] private static partial Regex TokenRx();

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    [HttpPost("deployments")]
    [AllowAnonymous]
    [EnableRateLimiting("ingest")]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Deployment(DeploymentReport r, CancellationToken ct)
    {
        var auth = Request.Headers.Authorization.ToString();
        var token = auth.StartsWith("Bearer ", StringComparison.Ordinal) ? auth[7..] : "";
        IngestToken? found = null;
        if (TokenRx().IsMatch(token))
        {
            // The lookup is by hash, so comparing the stored value leaks nothing about the token.
            var hash = Hash(token);
            found = await db.IngestTokens.SingleOrDefaultAsync(t => t.Sha256 == hash && t.RevokedAt == null, ct);
        }
        if (found is null)
        {
            await audit.WriteAsync(new AuditEntry("ingest.rejected", null, null, null, false, "invalid or revoked token", HttpContext.Connection.RemoteIpAddress?.ToString()), ct);
            return Unauthorized();
        }

        var errors = Validate(r);
        if (errors.Count > 0) return BadRequest(new ProblemDetails { Title = string.Join(" ", errors) });

        db.DeploymentEvents.Add(new DeploymentEvent
        {
            WorkspaceId = found.WorkspaceId,
            ProjectId = found.ProjectId,
            Environment = r.Environment,
            Digest = r.Digest,
            Commit = r.Commit,
            CommitTime = r.CommitTime,
            StartedAt = r.StartedAt,
            FinishedAt = r.FinishedAt,
            Success = r.Result == "success",
            BuildUrl = r.BuildUrl,
        });
        found.LastUsedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Accepted();
    }

    private static List<string> Validate(DeploymentReport r)
    {
        var errors = new List<string>();
        var now = DateTimeOffset.UtcNow;
        if (!EnvRx().IsMatch(r.Environment ?? "")) errors.Add("Invalid environment.");
        if (r.Result is not ("success" or "failure")) errors.Add("Result must be success or failure.");
        if (r.Digest is not null && !DigestRx().IsMatch(r.Digest)) errors.Add("Invalid digest.");
        if (r.Commit is not null && !CommitRx().IsMatch(r.Commit)) errors.Add("Invalid commit.");
        if (r.FinishedAt < r.StartedAt || r.FinishedAt > now.AddMinutes(5) || r.StartedAt < now.AddDays(-2)) errors.Add("Implausible start or finish time.");
        if (r.CommitTime is { } ctime && (ctime > now.AddMinutes(5) || ctime < now.AddYears(-5))) errors.Add("Implausible commit time.");
        if (r.BuildUrl is not null && !(Uri.TryCreate(r.BuildUrl, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) && r.BuildUrl.Length <= 2048 && string.IsNullOrEmpty(u.UserInfo)))
            errors.Add("Invalid build URL.");
        return errors;
    }
}

[ApiController]
[Route("api/workspaces/{workspaceId:guid}/pipelines/{id:guid}")]
public sealed class MetricsController(AppDbContext db, ICurrentUser currentUser, Auditor audit) : ControllerBase
{
    [HttpGet("metrics")]
    [Authorize(Policies.Viewer)]
    public async Task<MetricsResponse> Metrics(Guid workspaceId, Guid id, int days = 30, CancellationToken ct = default)
    {
        if (days is < 7 or > 365) throw new ArgumentException("Days must be between 7 and 365.");
        await Project(workspaceId, id, ct);
        var from = DateTimeOffset.UtcNow.AddDays(-days);
        var events = await db.DeploymentEvents
            .Where(e => e.ProjectId == id && e.WorkspaceId == workspaceId && e.FinishedAt > from)
            .Select(e => new DeployRecord(e.Environment, e.CommitTime, e.FinishedAt, e.Success))
            .ToListAsync(ct);
        return new MetricsResponse(days, DeliveryMetrics.Compute(events, DateTimeOffset.UtcNow, days));
    }

    [HttpGet("ingest-tokens")]
    [Authorize(Policies.Admin)]
    public async Task<IReadOnlyList<IngestTokenDto>> Tokens(Guid workspaceId, Guid id, CancellationToken ct)
    {
        await Project(workspaceId, id, ct);
        return await db.IngestTokens.Where(t => t.ProjectId == id && t.WorkspaceId == workspaceId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new IngestTokenDto(t.Id, t.Prefix, t.CreatedAt, t.LastUsedAt, t.RevokedAt))
            .ToListAsync(ct);
    }

    /// <summary>Creates a token; the value is returned once and only its hash is kept.</summary>
    [HttpPost("ingest-tokens")]
    [Authorize(Policies.Admin)]
    [EnableRateLimiting("expensive")]
    public async Task<IngestTokenCreated> CreateToken(Guid workspaceId, Guid id, CancellationToken ct)
    {
        var project = await Project(workspaceId, id, ct);
        var user = await currentUser.GetAsync(ct);
        var token = IngestController.TokenPrefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        var entity = new IngestToken
        {
            WorkspaceId = workspaceId,
            ProjectId = id,
            Prefix = token[..15],
            Sha256 = IngestController.Hash(token),
            CreatedBy = user.Id,
        };
        db.IngestTokens.Add(entity);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("ingest.token_created", workspaceId, project.Name, detail: entity.Prefix, ct: ct);
        return new IngestTokenCreated(entity.Id, entity.Prefix, token);
    }

    [HttpDelete("ingest-tokens/{tokenId:guid}")]
    [Authorize(Policies.Admin)]
    public async Task<IActionResult> RevokeToken(Guid workspaceId, Guid id, Guid tokenId, CancellationToken ct)
    {
        var project = await Project(workspaceId, id, ct);
        var token = await db.IngestTokens.SingleOrDefaultAsync(t => t.Id == tokenId && t.ProjectId == id && t.WorkspaceId == workspaceId, ct)
            ?? throw new NotFoundException("Token not found.");
        token.RevokedAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("ingest.token_revoked", workspaceId, project.Name, detail: token.Prefix, ct: ct);
        return NoContent();
    }

    private async Task<PipelineProject> Project(Guid workspaceId, Guid id, CancellationToken ct) =>
        await db.Projects.SingleOrDefaultAsync(p => p.Id == id && p.WorkspaceId == workspaceId, ct) ?? throw new NotFoundException("Pipeline not found.");

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
