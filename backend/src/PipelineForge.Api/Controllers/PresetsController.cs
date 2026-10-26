using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PipelineForge.Api.Auth;
using PipelineForge.Api.Contracts;
using PipelineForge.Api.Services;
using PipelineForge.Core.Presets;
using PipelineForge.Infrastructure.Persistence;

namespace PipelineForge.Api.Controllers;

[ApiController]
[Route("api/workspaces/{workspaceId:guid}/presets")]
public sealed partial class PresetsController(AppDbContext db, ICurrentUser currentUser, Auditor audit) : ControllerBase
{
    [GeneratedRegex(@"^[a-zA-Z]+(\.[a-zA-Z]+){0,4}$")] private static partial Regex LockPathRx();

    [HttpGet]
    [Authorize(Policies.Viewer)]
    public async Task<IReadOnlyList<PresetDto>> List(Guid workspaceId, CancellationToken ct)
    {
        var presets = await db.Presets.Where(p => p.WorkspaceId == workspaceId).OrderBy(p => p.Name).ToListAsync(ct);
        var ids = presets.Select(p => p.Id).ToList();
        var versions = await db.PresetVersions.Where(v => ids.Contains(v.PresetId)).ToListAsync(ct);
        return presets.Select(p =>
        {
            var v = versions.Single(x => x.PresetId == p.Id && x.Version == p.LatestVersion);
            return ToDto(p, v);
        }).ToList();
    }

    [HttpGet("{id:guid}/versions")]
    [Authorize(Policies.Viewer)]
    public async Task<ActionResult<IReadOnlyList<PresetDto>>> Versions(Guid workspaceId, Guid id, CancellationToken ct)
    {
        var p = await db.Presets.SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspaceId, ct);
        if (p is null) return NotFound();
        var versions = await db.PresetVersions.Where(v => v.PresetId == id).OrderByDescending(v => v.Version).ToListAsync(ct);
        return versions.Select(v => ToDto(p, v)).ToList();
    }

    [HttpPost]
    [Authorize(Policies.Maintainer)]
    public async Task<ActionResult<PresetDto>> Create(Guid workspaceId, SavePresetRequest r, CancellationToken ct)
    {
        var (configJson, locks) = await CheckAsync(workspaceId, r, ct);
        var user = await currentUser.GetAsync(ct);
        var name = Require.Name(r.Name, "Name");
        if (await db.Presets.AnyAsync(p => p.WorkspaceId == workspaceId && p.Name == name, ct))
            return Conflict(new ProblemDetails { Title = "A preset with that name exists." });
        var preset = new Preset { WorkspaceId = workspaceId, Name = name, Description = r.Description?.Trim() ?? "" };
        var version = new PresetVersion { PresetId = preset.Id, Version = 1, ConfigJson = configJson, LockedPathsJson = JsonSerializer.Serialize(locks), CreatedBy = user.Id };
        db.Presets.Add(preset);
        db.PresetVersions.Add(version);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("preset.create", workspaceId, name, detail: $"locks={string.Join(",", locks)}", ct: ct);
        return ToDto(preset, version);
    }

    /// <summary>Saving creates a new version; projects keep a reference to the version they used.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policies.Maintainer)]
    public async Task<ActionResult<PresetDto>> Update(Guid workspaceId, Guid id, SavePresetRequest r, CancellationToken ct)
    {
        var preset = await db.Presets.SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspaceId, ct);
        if (preset is null) return NotFound();
        var current = await db.PresetVersions.SingleAsync(v => v.PresetId == id && v.Version == preset.LatestVersion, ct);
        var (configJson, locks) = await CheckAsync(workspaceId, r, ct, current);
        var user = await currentUser.GetAsync(ct);
        preset.Name = Require.Name(r.Name, "Name");
        preset.Description = r.Description?.Trim() ?? "";
        preset.LatestVersion++;
        var version = new PresetVersion { PresetId = id, Version = preset.LatestVersion, ConfigJson = configJson, LockedPathsJson = JsonSerializer.Serialize(locks), CreatedBy = user.Id };
        db.PresetVersions.Add(version);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("preset.update", workspaceId, preset.Name, detail: $"v{version.Version} locks={string.Join(",", locks)}", ct: ct);
        return ToDto(preset, version);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policies.Admin)]
    public async Task<IActionResult> Delete(Guid workspaceId, Guid id, CancellationToken ct)
    {
        var preset = await db.Presets.SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspaceId, ct);
        if (preset is null) return NotFound();
        db.Presets.Remove(preset);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("preset.delete", workspaceId, preset.Name, ct: ct);
        return NoContent();
    }

    /// <summary>Only admins may add or remove locks, or change a locked value; maintainers edit the rest.</summary>
    private async Task<(string ConfigJson, List<string> Locks)> CheckAsync(Guid workspaceId, SavePresetRequest r, CancellationToken ct, PresetVersion? current = null)
    {
        if (r.Config.ValueKind != JsonValueKind.Object) throw new ArgumentException("Preset config must be a JSON object.");
        var json = r.Config.GetRawText();
        if (json.Length > 100_000) throw new ArgumentException("Preset too large.");
        var probe = PresetMerger.Apply(new Core.Model.PipelineConfig(), json); // throws when the shape does not fit the model
        var locks = (r.LockedPaths ?? []).Select(p => p.Trim()).Distinct().ToList();
        if (locks.Count > 30 || locks.Any(p => !LockPathRx().IsMatch(p))) throw new ArgumentException("Locked paths look like 'compliance.profile'.");

        var currentLocks = current is null ? [] : JsonSerializer.Deserialize<List<string>>(current.LockedPathsJson) ?? [];
        var locksChanged = !locks.Order().SequenceEqual(currentLocks.Order());
        var lockedValuesChanged = current is not null && currentLocks.Count > 0
            && PresetMerger.Violations(probe, current.ConfigJson, currentLocks).Count > 0;
        if (locksChanged || lockedValuesChanged)
        {
            var user = await currentUser.GetAsync(ct);
            var isAdmin = await db.Memberships.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == user.Id && m.Role == WorkspaceRole.Admin, ct);
            if (!isAdmin) throw new ForbiddenException("Only workspace admins can change locked preset settings.");
        }
        return (json, locks);
    }

    private static PresetDto ToDto(Preset p, PresetVersion v) =>
        new(p.Id, p.Name, p.Description, v.Version, JsonDocument.Parse(v.ConfigJson).RootElement.Clone(),
            JsonSerializer.Deserialize<List<string>>(v.LockedPathsJson) ?? [], v.CreatedAt);
}
