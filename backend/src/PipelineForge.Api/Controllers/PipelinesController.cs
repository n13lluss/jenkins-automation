using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PipelineForge.Api.Auth;
using PipelineForge.Api.Contracts;
using PipelineForge.Api.Services;
using PipelineForge.Core;
using PipelineForge.Core.Compliance;
using PipelineForge.Core.Generation;
using PipelineForge.Core.Importing;
using PipelineForge.Core.Model;
using PipelineForge.Core.Scanning;
using PipelineForge.Core.Scm;
using PipelineForge.Core.Security;
using PipelineForge.Infrastructure;
using PipelineForge.Infrastructure.Jenkins;
using PipelineForge.Infrastructure.Persistence;

namespace PipelineForge.Api.Controllers;

[ApiController]
[Route("api/workspaces/{workspaceId:guid}")]
public sealed class PipelinesController(
    AppDbContext db, ICurrentUser currentUser, Auditor audit, ConnectionResolver resolver, GenerationService generation,
    ScmProviders scm, AiProviders ai, JenkinsClient jenkins) : ControllerBase
{
    private const int MaxAiPromptChars = 400_000;

    [HttpPost("scans")]
    [Authorize(Policies.Maintainer)]
    [EnableRateLimiting("expensive")]
    public async Task<ScanResponse> Scan(Guid workspaceId, ScanRequest r, CancellationToken ct)
    {
        var repo = Require.Repository(r.Repository);
        var branch = Require.Branch(r.Branch);
        var (_, info) = await resolver.ScmAsync(workspaceId, r.ConnectionId, ct);
        var provider = scm.Get(info.Kind);
        var profile = await RepoScanner.ScanAsync(new ScmRepoReader(provider, info, repo, branch), repo, ct);
        var defaultBranch = (await provider.ListRepositoriesAsync(info, repo.Split('/')[1], ct))
            .FirstOrDefault(x => string.Equals(x.FullName, repo, StringComparison.OrdinalIgnoreCase))?.DefaultBranch;
        var suggestion = SuggestionEngine.Suggest(profile, repo, defaultBranch);
        await audit.WriteAsync("repo.scan", workspaceId, $"{repo}@{branch}", detail: $"{profile.Modules.Count} modules, {profile.FileCount} files", ct: ct);
        return new ScanResponse(profile, suggestion.Config, suggestion.Reasons);
    }

    [HttpPost("imports/jenkinsfile")]
    [Authorize(Policies.Maintainer)]
    [RequestSizeLimit(512 * 1024)]
    public async Task<ImportResult> Import(Guid workspaceId, ImportRequest r, CancellationToken ct)
    {
        string content;
        string name;
        if (r.Content is { Length: > 0 })
        {
            content = r.Content;
            name = r.Name ?? "app";
        }
        else
        {
            if (r.ConnectionId is not { } connId) throw new ArgumentException("Paste a Jenkinsfile or pick a repository.");
            var repo = Require.Repository(r.Repository);
            var branch = Require.Branch(r.Branch);
            var path = Require.RelativePath(r.Path ?? "Jenkinsfile");
            var (_, info) = await resolver.ScmAsync(workspaceId, connId, ct);
            content = await scm.Get(info.Kind).GetFileAsync(info, repo, branch, path, JenkinsfileImporter.MaxLength, ct)
                      ?? throw new NotFoundException("No Jenkinsfile at that path, or it is too large.");
            name = repo;
        }
        var result = JenkinsfileImporter.Import(content, name);
        await audit.WriteAsync("jenkinsfile.import", workspaceId, name, detail: $"{result.Stages.Count} stages, {result.Config.CustomStages.Count} custom", ct: ct);
        return result;
    }

    /// <summary>Stateless generation for the wizard preview; nothing is stored.</summary>
    [HttpPost("pipelines/preview")]
    [Authorize(Policies.Maintainer)]
    [RequestSizeLimit(512 * 1024)]
    public async Task<GenerationResponse> Preview(Guid workspaceId, PreviewRequest r, CancellationToken ct)
    {
        var config = r.Config;
        if (r.BuiltInPresetId is { } builtIn)
            config = Core.Presets.PresetMerger.Apply(config, (Core.Presets.BuiltInPresets.Find(builtIn) ?? throw new NotFoundException("Built-in preset not found.")).ConfigJson);
        var preset = await generation.PresetAsync(workspaceId, r.PresetId, ct);
        config = GenerationService.ApplyPreset(config, preset, r.ApplyPreset);
        return GenerationService.Generate(config, preset, r.Files);
    }

    [HttpGet("pipelines")]
    [Authorize(Policies.Viewer)]
    public async Task<IReadOnlyList<ProjectSummaryDto>> List(Guid workspaceId, CancellationToken ct) =>
        await db.Projects.Where(p => p.WorkspaceId == workspaceId).OrderByDescending(p => p.UpdatedAt)
            .Select(p => new ProjectSummaryDto(p.Id, p.Name, p.Source, p.Repository, p.Branch, p.UpdatedAt))
            .ToListAsync(ct);

    [HttpGet("pipelines/{id:guid}")]
    [Authorize(Policies.Viewer)]
    public async Task<ProjectDto> Get(Guid workspaceId, Guid id, CancellationToken ct) => ToDto(await Load(workspaceId, id, ct));

    [HttpPost("pipelines")]
    [Authorize(Policies.Maintainer)]
    [RequestSizeLimit(512 * 1024)]
    public async Task<ProjectDto> Create(Guid workspaceId, SaveProjectRequest r, CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        var project = new PipelineProject { WorkspaceId = workspaceId, Name = "x", Source = "blank", ConfigJson = "{}", CreatedBy = user.Id };
        await Apply(workspaceId, project, r, ct);
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("pipeline.create", workspaceId, project.Name, detail: project.Source, ct: ct);
        return ToDto(project);
    }

    [HttpPut("pipelines/{id:guid}")]
    [Authorize(Policies.Maintainer)]
    [RequestSizeLimit(512 * 1024)]
    public async Task<ProjectDto> Update(Guid workspaceId, Guid id, SaveProjectRequest r, CancellationToken ct)
    {
        var project = await Load(workspaceId, id, ct);
        await Apply(workspaceId, project, r, ct);
        project.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("pipeline.update", workspaceId, project.Name, ct: ct);
        return ToDto(project);
    }

    [HttpDelete("pipelines/{id:guid}")]
    [Authorize(Policies.Maintainer)]
    public async Task<IActionResult> Delete(Guid workspaceId, Guid id, CancellationToken ct)
    {
        var project = await Load(workspaceId, id, ct);
        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("pipeline.delete", workspaceId, project.Name, ct: ct);
        return NoContent();
    }

    [HttpGet("pipelines/{id:guid}/runs")]
    [Authorize(Policies.Viewer)]
    public async Task<IReadOnlyList<RunDto>> Runs(Guid workspaceId, Guid id, CancellationToken ct)
    {
        var runs = await db.GenerationRuns.Where(r => r.WorkspaceId == workspaceId && r.ProjectId == id)
            .OrderByDescending(r => r.CreatedAt).Take(50).ToListAsync(ct);
        return runs.Select(r => new RunDto(r.Id, r.CreatedAt, r.ConfigSha256, r.PullRequestUrl, JsonDocument.Parse(r.FilesJson).RootElement.Clone())).ToList();
    }

    [HttpGet("pipelines/{id:guid}/download")]
    [Authorize(Policies.Viewer)]
    public async Task<IActionResult> Download(Guid workspaceId, Guid id, [FromQuery] string? files, CancellationToken ct)
    {
        var project = await Load(workspaceId, id, ct);
        var (result, _) = await GenerateFor(workspaceId, project, Split(files), ct);
        if (!result.Success) return UnprocessableEntity(result);
        await Record(workspaceId, project, result, null, ct);
        await audit.WriteAsync("pipeline.download", workspaceId, project.Name, detail: $"{result.Files.Count} files", ct: ct);
        return File(GenerationService.Zip(result.Files), "application/zip", $"{project.Name}-pipeline.zip");
    }

    [HttpPost("pipelines/{id:guid}/pull-request")]
    [Authorize(Policies.Maintainer)]
    [EnableRateLimiting("expensive")]
    public async Task<ActionResult<PullRequestResponse>> PullRequest(Guid workspaceId, Guid id, PullRequestCreate r, CancellationToken ct)
    {
        var project = await Load(workspaceId, id, ct);
        if (project.ScmConnectionId is not { } connId || project.Repository is null)
            return BadRequest(new ProblemDetails { Title = "This pipeline is not linked to a repository. Download the files instead." });
        var (result, _) = await GenerateFor(workspaceId, project, r.Files, ct);
        if (!result.Success) return UnprocessableEntity(result);

        var (_, info) = await resolver.ScmAsync(workspaceId, connId, ct);
        var provider = scm.Get(info.Kind);
        var target = Require.Branch(r.TargetBranch ?? project.Branch ?? "main");
        var branch = $"pipelineforge/{project.Name}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var user = await currentUser.GetAsync(ct);
        // Shared library layout: library/ files go to the library repository in a pull request of their own.
        var appFiles = result.Files.Where(f => !f.Path.StartsWith(LibraryWriter.Prefix, StringComparison.Ordinal)).ToList();
        var libraryFiles = result.Files.Where(f => f.Path.StartsWith(LibraryWriter.Prefix, StringComparison.Ordinal))
            .Select(f => new FileChange(f.Path[LibraryWriter.Prefix.Length..], f.Content)).ToList();
        var library = result.EffectiveConfig.Library;
        try
        {
            string? libraryUrl = null, libraryNote = null;
            if (libraryFiles.Count > 0 && library.Repository is { } libraryRepo)
            {
                await provider.CreateBranchAsync(info, libraryRepo, branch, library.DefaultVersion, ct);
                await provider.CommitFilesAsync(info, libraryRepo, branch, $"Update pipeline {project.Name} generated by PipelineForge", libraryFiles, ct);
                var libraryPr = await provider.CreatePullRequestAsync(info, new PullRequestRequest(libraryRepo, branch, library.DefaultVersion,
                    $"Pipeline {project.Name}", PullRequestBody(result, user.DisplayName)), ct);
                libraryUrl = libraryPr.Url;
                await audit.WriteAsync("pipeline.pull_request", workspaceId, libraryRepo, detail: libraryPr.Url, ct: ct);
            }
            else if (libraryFiles.Count > 0)
            {
                libraryNote = "No library repository is set, so the library files were not committed. Download them or set the library repository.";
            }

            PullRequestResult? pr = null;
            if (appFiles.Count > 0)
            {
                await provider.CreateBranchAsync(info, project.Repository, branch, target, ct);
                await provider.CommitFilesAsync(info, project.Repository, branch, "Add Jenkins pipeline generated by PipelineForge",
                    appFiles.Select(f => new FileChange(f.Path, f.Content)).ToList(), ct);
                pr = await provider.CreatePullRequestAsync(info, new PullRequestRequest(project.Repository, branch, target,
                    $"Add Jenkins pipeline for {project.Name}", PullRequestBody(result, user.DisplayName)), ct);
                await audit.WriteAsync("pipeline.pull_request", workspaceId, project.Repository, detail: pr.Url, ct: ct);
            }
            await Record(workspaceId, project, result, pr?.Url ?? libraryUrl, ct);
            return new PullRequestResponse(pr?.Url ?? "", branch, libraryUrl, libraryNote);
        }
        catch (Exception e)
        {
            await audit.WriteAsync("pipeline.pull_request", workspaceId, project.Repository, success: false, detail: e.GetType().Name, ct: ct);
            throw;
        }
    }

    /// <summary>
    /// Compares the generated files with the repository copies (the library repository for library files), so an
    /// existing pipeline can be updated with only the files that changed.
    /// </summary>
    [HttpGet("pipelines/{id:guid}/drift")]
    [Authorize(Policies.Maintainer)]
    [EnableRateLimiting("expensive")]
    public async Task<ActionResult<IReadOnlyList<Core.Drift.FileDrift>>> Drift(Guid workspaceId, Guid id, CancellationToken ct)
    {
        const int maxFileBytes = 200_000;
        var project = await Load(workspaceId, id, ct);
        if (project.ScmConnectionId is not { } connId || project.Repository is null)
            return BadRequest(new ProblemDetails { Title = "Link the pipeline to a repository to compare it." });
        var (result, _) = await GenerateFor(workspaceId, project, null, ct);
        if (!result.Success) return UnprocessableEntity(result);

        var (_, info) = await resolver.ScmAsync(workspaceId, connId, ct);
        var provider = scm.Get(info.Kind);
        var library = result.EffectiveConfig.Library;
        var branch = project.Branch ?? "main";
        var drift = new List<Core.Drift.FileDrift>();
        foreach (var f in result.Files)
        {
            var inLibrary = f.Path.StartsWith(LibraryWriter.Prefix, StringComparison.Ordinal);
            if (inLibrary && library.Repository is null)
            {
                drift.Add(new(f.Path, Core.Drift.DriftStatus.Unavailable, null, null, "No library repository is set."));
                continue;
            }
            var (repo, @ref, path) = inLibrary
                ? (library.Repository!, library.DefaultVersion, f.Path[LibraryWriter.Prefix.Length..])
                : (project.Repository, branch, f.Path);
            try
            {
                var current = await provider.GetFileAsync(info, repo, @ref, path, maxFileBytes, ct);
                drift.Add(Core.Drift.DriftComparer.Compare(f.Path, f.Content, current));
            }
            catch (PipelineForge.Infrastructure.Scm.ScmException e)
            {
                drift.Add(new(f.Path, Core.Drift.DriftStatus.Unavailable, null, null, e.Message));
            }
        }
        await audit.WriteAsync("pipeline.drift", workspaceId, project.Repository,
            detail: $"{drift.Count(d => d.Status is Core.Drift.DriftStatus.Changed or Core.Drift.DriftStatus.Customised or Core.Drift.DriftStatus.Missing)} of {drift.Count} files differ", ct: ct);
        return drift;
    }

    /// <summary>Makes the SCM require the Jenkins build (and approvals) before merging into the deploy branches.</summary>
    [HttpPost("pipelines/{id:guid}/branch-protection")]
    [Authorize(Policies.Maintainer)]
    [EnableRateLimiting("expensive")]
    public async Task<ActionResult<IReadOnlyList<BranchRuleResult>>> BranchProtection(Guid workspaceId, Guid id, BranchProtectionRequest r, CancellationToken ct)
    {
        var project = await Load(workspaceId, id, ct);
        if (project.ScmConnectionId is not { } connId || project.Repository is null)
            return BadRequest(new ProblemDetails { Title = "Link the pipeline to a repository first." });
        if (r.Approvals is < 0 or > 10) throw new ArgumentException("Approvals must be between 0 and 10.");
        var config = JsonSerializer.Deserialize<PipelineConfig>(project.ConfigJson, JsonDefaults.Options)!;
        var branches = (r.Branches is { Count: > 0 } ? r.Branches : config.Environments.Select(e => e.BranchPattern).ToList())
            .Distinct().ToList();
        if (branches.Count > 20 || branches.Any(b => !Core.Validation.ConfigValidator.IsBranchPattern(b))) throw new ArgumentException("Invalid branch list.");
        var (_, info) = await resolver.ScmAsync(workspaceId, connId, ct);
        var results = await scm.Get(info.Kind).RequireBuildsAsync(info, project.Repository, branches, r.Approvals, ct);
        await audit.WriteAsync("scm.branch_protection", workspaceId, project.Repository, results.All(x => x.Ok),
            string.Join("; ", results.Select(x => $"{x.Branch}:{x.Rule}={(x.Ok ? "ok" : "failed")}")), ct);
        return Ok(results);
    }

    [HttpPost("pipelines/{id:guid}/validate")]
    [Authorize(Policies.Maintainer)]
    [EnableRateLimiting("expensive")]
    public async Task<ActionResult<JenkinsValidation>> Validate(Guid workspaceId, Guid id, ValidateRequest r, CancellationToken ct)
    {
        var project = await Load(workspaceId, id, ct);
        // The linter needs the full declarative pipeline, not the thin Jenkinsfile of the shared library layout.
        var (result, _) = await GenerateFor(workspaceId, project, ["Jenkinsfile"], ct, inRepoLayout: true);
        if (!result.Success) return UnprocessableEntity(result);
        var conn = await resolver.JenkinsAsync(workspaceId, r.JenkinsConnectionId, ct);
        var validation = await jenkins.ValidateAsync(conn, result.Files.Single().Content, ct);
        await audit.WriteAsync("jenkins.validate", workspaceId, project.Name, validation.Valid, ct: ct);
        return validation;
    }

    [HttpPost("pipelines/{id:guid}/jenkins-job")]
    [Authorize(Policies.Maintainer)]
    [EnableRateLimiting("expensive")]
    public async Task<ActionResult<JenkinsJobResponse>> JenkinsJob(Guid workspaceId, Guid id, JenkinsJobRequest r, CancellationToken ct)
    {
        var project = await Load(workspaceId, id, ct);
        if (project.ScmConnectionId is not { } connId || project.Repository is null)
            return BadRequest(new ProblemDetails { Title = "Link the pipeline to a repository first." });
        if (!Core.Validation.ConfigValidator.IsIdentifier(r.ScmCredentialId)) throw new ArgumentException("Invalid Jenkins credential ID.");
        var scmConn = await db.Connections.SingleAsync(c => c.Id == connId && c.WorkspaceId == workspaceId, ct);
        var kind = Enum.Parse<ScmKind>(scmConn.Provider);
        var serverUrl = kind switch
        {
            ScmKind.BitbucketServer => scmConn.BaseUrl,
            ScmKind.GitHub when !scmConn.BaseUrl.Contains("api.github.com") => scmConn.BaseUrl,
            _ => null,
        };
        var conn = await resolver.JenkinsAsync(workspaceId, r.JenkinsConnectionId, ct);
        var config = JsonSerializer.Deserialize<PipelineConfig>(project.ConfigJson, JsonDefaults.Options)!;
        JobLibrary? library = null;
        if (config.Layout == PipelineLayout.SharedLibrary)
        {
            var lib = config.Library;
            if (lib.Repository is null) return BadRequest(new ProblemDetails { Title = "Set the library repository first: the job loads the pipeline from it." });
            var errors = Core.Validation.ConfigValidator.Validate(config).Where(e => e.Path.StartsWith("library", StringComparison.Ordinal)).ToList();
            if (errors.Count > 0) return BadRequest(new ProblemDetails { Title = errors[0].Message });
            library = new JobLibrary(lib.Name, lib.Repository, lib.DefaultVersion);
        }
        var url = await jenkins.UpsertMultibranchJobAsync(conn, new MultibranchJob(r.JobName, kind, project.Repository, r.ScmCredentialId, serverUrl) { Library = library }, ct);
        string? scheduledUrl = null;
        if (r.CreateScheduledJob)
        {
            if (!config.Security.Dast.Schedule.Enabled) return BadRequest(new ProblemDetails { Title = "Scheduled scans are not enabled for this pipeline." });
            scheduledUrl = await jenkins.UpsertMultibranchJobAsync(conn,
                new MultibranchJob($"{r.JobName}-scheduled", kind, project.Repository, r.ScmCredentialId, serverUrl, "Jenkinsfile.scheduled") { Library = library }, ct);
        }
        await audit.WriteAsync("jenkins.job", workspaceId, r.JobName, detail: scheduledUrl is null ? url : $"{url} {scheduledUrl}", ct: ct);
        return new JenkinsJobResponse(url, scheduledUrl);
    }

    [HttpPost("pipelines/{id:guid}/ai-review")]
    [Authorize(Policies.Maintainer)]
    [EnableRateLimiting("expensive")]
    public async Task<ActionResult<Core.Ai.AiReviewResult>> AiReview(Guid workspaceId, Guid id, AiReviewRequest r, CancellationToken ct)
    {
        var ws = await db.Workspaces.SingleAsync(w => w.Id == workspaceId, ct);
        if (!ws.AiEnabled) return BadRequest(new ProblemDetails { Title = "AI is disabled for this workspace. An admin can enable it." });
        var project = await Load(workspaceId, id, ct);
        var (result, _) = await GenerateFor(workspaceId, project, null, ct);
        if (!result.Success) return UnprocessableEntity(result);

        // Data policy: the profile and generated files only, never repository source; secrets redacted.
        var prompt = new StringBuilder();
        prompt.AppendLine("<repository_profile>").AppendLine(project.ProfileJson ?? "(blank project, no scan)").AppendLine("</repository_profile>");
        foreach (var f in result.Files)
            prompt.AppendLine($"<file path=\"{f.Path}\">").AppendLine(f.Content).AppendLine("</file>");
        var text = SecretRedactor.Redact(prompt.ToString());
        if (text.Length > MaxAiPromptChars) return BadRequest(new ProblemDetails { Title = "The generated pipeline is too large for an AI review." });

        var conn = await resolver.AiAsync(workspaceId, r.AiConnectionId, ct);
        var review = await ai.Get(conn.Provider).ReviewPipelineAsync(conn, text, ct);
        await audit.WriteAsync("ai.call", workspaceId, project.Name, review.Problem is null,
            $"{conn.Provider}/{conn.Model} chars={text.Length} sha256={review.PayloadSha256}", ct);
        return review;
    }

    private async Task<PipelineProject> Load(Guid workspaceId, Guid id, CancellationToken ct) =>
        await db.Projects.SingleOrDefaultAsync(p => p.Id == id && p.WorkspaceId == workspaceId, ct) ?? throw new NotFoundException("Pipeline not found.");

    private async Task Apply(Guid workspaceId, PipelineProject project, SaveProjectRequest r, CancellationToken ct)
    {
        project.Name = Core.Validation.ConfigValidator.IsName(r.Name) ? r.Name : throw new ArgumentException("Name: lowercase letters, digits, '.', '_' and '-'.");
        project.Source = r.Source is "scan" or "blank" or "import" ? r.Source : throw new ArgumentException("Source must be scan, blank or import.");
        if (r.ScmConnectionId is { } connId)
        {
            if (!await db.Connections.AnyAsync(c => c.Id == connId && c.WorkspaceId == workspaceId && c.Kind == ConnectionKind.Scm, ct))
                throw new NotFoundException("Connection not found.");
            project.ScmConnectionId = connId;
            project.Repository = Require.Repository(r.Repository);
            project.Branch = r.Branch is null ? null : Require.Branch(r.Branch);
        }
        else
        {
            project.ScmConnectionId = null;
            project.Repository = null;
            project.Branch = null;
        }
        var preset = await generation.PresetAsync(workspaceId, r.PresetId, ct);
        project.PresetId = preset?.Id;
        project.PresetVersion = preset?.Version;
        project.ConfigJson = GenerationService.ConfigJson(GenerationService.ApplyPreset(r.Config, preset, merge: false));
        if (r.Profile is not null) project.ProfileJson = JsonSerializer.Serialize(r.Profile, JsonDefaults.Options);
    }

    private async Task<(GenerationResponse Result, LoadedPreset? Preset)> GenerateFor(Guid workspaceId, PipelineProject project, IReadOnlyCollection<string>? files, CancellationToken ct, bool inRepoLayout = false)
    {
        var config = JsonSerializer.Deserialize<PipelineConfig>(project.ConfigJson, JsonDefaults.Options)!;
        var preset = project.PresetId is null ? null : await generation.PresetAsync(workspaceId, project.PresetId, ct);
        config = GenerationService.ApplyPreset(config, preset, merge: false);
        return (GenerationService.Generate(config, preset, files, inRepoLayout), preset);
    }

    private async Task Record(Guid workspaceId, PipelineProject project, GenerationResponse result, string? prUrl, CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        db.GenerationRuns.Add(new GenerationRun
        {
            ProjectId = project.Id,
            WorkspaceId = workspaceId,
            ConfigSha256 = GenerationService.Sha256(GenerationService.ConfigJson(result.EffectiveConfig)),
            FilesJson = JsonSerializer.Serialize(result.Files.Select(f => new { path = f.Path, sha256 = GenerationService.Sha256(f.Content) })),
            PullRequestUrl = prUrl,
            CreatedBy = user.Id,
        });
        await db.SaveChangesAsync(ct);
    }

    private static List<string>? Split(string? files) =>
        string.IsNullOrWhiteSpace(files) ? null : files.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string PullRequestBody(GenerationResponse result, string author)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Generated by PipelineForge {Core.Drift.VersionMarker.Version} for {author}. See `docs/PIPELINE.md` for details.").AppendLine();
        sb.AppendLine("**Files**").AppendLine();
        foreach (var f in result.Files) sb.AppendLine($"- `{f.Path}`");
        if (result.Credentials.Count > 0)
        {
            sb.AppendLine().AppendLine("**Jenkins credentials to create**").AppendLine();
            foreach (var c in result.Credentials) sb.AppendLine($"- `{c.Id}` ({c.Kind}): {c.Purpose}");
        }
        if (result.Compliance.Changes.Count > 0)
        {
            sb.AppendLine().AppendLine("**Compliance**").AppendLine();
            foreach (var c in result.Compliance.Changes) sb.AppendLine($"- {c}");
        }
        return sb.ToString();
    }

    private static ProjectDto ToDto(PipelineProject p) => new(p.Id, p.Name, p.Source, p.ScmConnectionId, p.Repository, p.Branch,
        p.PresetId, p.PresetVersion,
        JsonSerializer.Deserialize<PipelineConfig>(p.ConfigJson, JsonDefaults.Options)!,
        p.ProfileJson is null ? null : JsonSerializer.Deserialize<RepoProfile>(p.ProfileJson, JsonDefaults.Options),
        p.CreatedAt, p.UpdatedAt);
}

[ApiController]
[Route("api/meta")]
public sealed class MetaController : ControllerBase
{
    [HttpGet]
    public MetaDto Get() => new(
        Enum.GetNames<Ecosystem>(),
        Enum.GetValues<Ecosystem>().ToDictionary(e => e.ToString(), ToolImages.DefaultVersion),
        ToolImages.Tools,
        ComplianceEnforcer.Iso27001Controls);
}
