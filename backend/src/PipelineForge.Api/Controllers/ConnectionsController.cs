using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PipelineForge.Api.Auth;
using PipelineForge.Api.Contracts;
using PipelineForge.Api.Services;
using PipelineForge.Core.Ai;
using PipelineForge.Core.Model;
using PipelineForge.Core.Scm;
using PipelineForge.Infrastructure;
using PipelineForge.Infrastructure.Jenkins;
using PipelineForge.Infrastructure.Net;
using PipelineForge.Infrastructure.Persistence;
using PipelineForge.Infrastructure.Secrets;
using Microsoft.Extensions.Options;

namespace PipelineForge.Api.Controllers;

[ApiController]
[Route("api/workspaces/{workspaceId:guid}/connections")]
public sealed class ConnectionsController(
    AppDbContext db, ISecretStore secrets, ICurrentUser currentUser, Auditor audit, ConnectionResolver resolver,
    ScmProviders scm, AiProviders ai, JenkinsClient jenkins, IOptions<OutboundOptions> outbound) : ControllerBase
{
    private static ConnectionDto Dto(Connection c) =>
        new(c.Id, c.Kind, c.Name, c.Provider, c.BaseUrl, c.Username, c.Scope, c.Model, c.CreatedAt);

    [HttpGet]
    [Authorize(Policies.Viewer)]
    public async Task<IReadOnlyList<ConnectionDto>> List(Guid workspaceId, CancellationToken ct) =>
        (await db.Connections.Where(c => c.WorkspaceId == workspaceId).OrderBy(c => c.Name).ToListAsync(ct)).Select(Dto).ToList();

    [HttpPost]
    [Authorize(Policies.Admin)]
    public async Task<ActionResult<ConnectionDto>> Create(Guid workspaceId, CreateConnectionRequest r, CancellationToken ct)
    {
        var provider = r.Kind switch
        {
            ConnectionKind.Scm => Enum.TryParse<ScmKind>(r.Provider, out var s) ? s.ToString() : throw new ArgumentException("Unknown SCM provider."),
            ConnectionKind.Ai => Enum.TryParse<AiProvider>(r.Provider, out var a) ? a.ToString() : throw new ArgumentException("Unknown AI provider."),
            _ => "jenkins",
        };
        var baseUrl = r.Kind == ConnectionKind.Ai && string.IsNullOrWhiteSpace(r.BaseUrl) ? "" : Require.Url(r.BaseUrl, "Base URL");
        if (baseUrl.Length > 0) OutboundGuard.ValidateUrl(new Uri(baseUrl), outbound.Value);
        if (r.Kind == ConnectionKind.Jenkins && string.IsNullOrWhiteSpace(r.Username)) throw new ArgumentException("Jenkins needs the user name that owns the API token.");
        if (r.Kind == ConnectionKind.Ai && provider == nameof(AiProvider.AzureOpenAi) && baseUrl.Length == 0) throw new ArgumentException("Azure OpenAI needs the resource URL.");

        var connection = new Connection
        {
            WorkspaceId = workspaceId,
            Kind = r.Kind,
            Name = Require.Name(r.Name, "Name"),
            Provider = provider,
            BaseUrl = baseUrl,
            Username = Require.Optional(r.Username, "Username"),
            Scope = Require.Optional(r.Scope, "Workspace/organisation"),
            Model = r.Kind == ConnectionKind.Ai ? Require.Optional(r.Model, "Model") ?? "claude-opus-5-5" : null,
            SecretRef = await secrets.SetAsync(workspaceId, Require.Secret(r.Secret), ct),
            CreatedBy = (await currentUser.GetAsync(ct)).Id,
        };
        db.Connections.Add(connection);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("connection.create", workspaceId, $"{connection.Kind}:{connection.Name}", detail: connection.BaseUrl, ct: ct);
        return Dto(connection);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policies.Admin)]
    public async Task<IActionResult> Delete(Guid workspaceId, Guid id, CancellationToken ct)
    {
        var c = await db.Connections.SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspaceId, ct);
        if (c is null) return NotFound();
        await secrets.DeleteAsync(c.SecretRef, ct);
        db.Connections.Remove(c);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("connection.delete", workspaceId, $"{c.Kind}:{c.Name}", ct: ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/test")]
    [Authorize(Policies.Maintainer)]
    [EnableRateLimiting("expensive")]
    public async Task<TestResult> Test(Guid workspaceId, Guid id, CancellationToken ct)
    {
        var c = await db.Connections.SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspaceId, ct)
                ?? throw new NotFoundException("Connection not found.");
        string message;
        switch (c.Kind)
        {
            case ConnectionKind.Scm:
                var (_, info) = await resolver.ScmAsync(workspaceId, id, ct);
                message = $"Connected as {await scm.Get(info.Kind).WhoAmIAsync(info, ct)}.";
                break;
            case ConnectionKind.Jenkins:
                var j = await resolver.JenkinsAsync(workspaceId, id, ct);
                message = $"Connected as {await jenkins.WhoAmIAsync(j, ct)}.";
                break;
            default:
                var ws = await db.Workspaces.SingleAsync(w => w.Id == workspaceId, ct);
                if (!ws.AiEnabled) return new TestResult(false, "AI is disabled for this workspace. An admin can enable it.");
                var a = await resolver.AiAsync(workspaceId, id, ct);
                var result = await ai.Get(a.Provider).ReviewPipelineAsync(a, "Connection test. Reply with an empty suggestions list and the summary \"ok\".", ct);
                await audit.WriteAsync("ai.call", workspaceId, c.Name, result.Problem is null, $"connection test sha256={result.PayloadSha256}", ct);
                return new TestResult(result.Problem is null, result.Problem ?? "The AI provider answered.");
        }
        await audit.WriteAsync("connection.test", workspaceId, c.Name, ct: ct);
        return new TestResult(true, message);
    }

    [HttpGet("{id:guid}/repos")]
    [Authorize(Policies.Maintainer)]
    public async Task<IReadOnlyList<ScmRepository>> Repositories(Guid workspaceId, Guid id, [FromQuery] string? q, CancellationToken ct)
    {
        var (_, info) = await resolver.ScmAsync(workspaceId, id, ct);
        return await scm.Get(info.Kind).ListRepositoriesAsync(info, q is null ? null : Require.Name(q, "Query"), ct);
    }

    [HttpGet("{id:guid}/branches")]
    [Authorize(Policies.Maintainer)]
    public async Task<IReadOnlyList<ScmBranch>> Branches(Guid workspaceId, Guid id, [FromQuery] string repo, CancellationToken ct)
    {
        var (_, info) = await resolver.ScmAsync(workspaceId, id, ct);
        return await scm.Get(info.Kind).ListBranchesAsync(info, Require.Repository(repo), ct);
    }
}
