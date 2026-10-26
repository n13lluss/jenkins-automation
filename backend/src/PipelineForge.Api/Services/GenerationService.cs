using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PipelineForge.Api.Contracts;
using PipelineForge.Core;
using PipelineForge.Core.Generation;
using PipelineForge.Core.Model;
using PipelineForge.Core.Presets;
using PipelineForge.Infrastructure.Persistence;

namespace PipelineForge.Api.Services;

public sealed record LoadedPreset(Guid Id, int Version, string ConfigJson, IReadOnlyList<string> LockedPaths);

/// <summary>Preset application, lock enforcement and generation, shared by preview and saved projects.</summary>
public sealed class GenerationService(AppDbContext db)
{
    public async Task<LoadedPreset?> PresetAsync(Guid workspaceId, Guid? presetId, CancellationToken ct)
    {
        if (presetId is null) return null;
        var preset = await db.Presets.SingleOrDefaultAsync(p => p.Id == presetId && p.WorkspaceId == workspaceId, ct)
                     ?? throw new NotFoundException("Preset not found.");
        var version = await db.PresetVersions.SingleAsync(v => v.PresetId == preset.Id && v.Version == preset.LatestVersion, ct);
        return new LoadedPreset(preset.Id, version.Version, version.ConfigJson,
            JsonSerializer.Deserialize<List<string>>(version.LockedPathsJson) ?? []);
    }

    /// <summary>Optionally merges the preset, then always re-applies its locked values.</summary>
    public static PipelineConfig ApplyPreset(PipelineConfig config, LoadedPreset? preset, bool merge)
    {
        if (preset is null) return config;
        if (merge) config = PresetMerger.Apply(config, preset.ConfigJson);
        return preset.LockedPaths.Count == 0 ? config : PresetMerger.EnforceLocks(config, preset.ConfigJson, preset.LockedPaths);
    }

    public static GenerationResponse Generate(PipelineConfig config, LoadedPreset? preset, IReadOnlyCollection<string>? files)
    {
        var r = PipelineGenerator.Generate(config, files);
        return new GenerationResponse(r.Success, r.Errors, r.Files, r.Compliance, r.Credentials, r.Plugins, r.EffectiveConfig,
            preset?.LockedPaths ?? []);
    }

    public static byte[] Zip(IEnumerable<GeneratedFile> files)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var f in files)
            {
                // Paths come from the generator; checked again so an archive can never escape its folder.
                if (f.Path.StartsWith('/') || f.Path.Split('/').Any(s => s is ".." or "")) throw new InvalidOperationException("Unsafe path in file set.");
                var entry = zip.CreateEntry(f.Path, CompressionLevel.Optimal);
                using var stream = entry.Open();
                stream.Write(Encoding.UTF8.GetBytes(f.Content));
            }
        }
        return buffer.ToArray();
    }

    public static string Sha256(string text) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static string ConfigJson(PipelineConfig config) => JsonSerializer.Serialize(config, JsonDefaults.Options);
}
