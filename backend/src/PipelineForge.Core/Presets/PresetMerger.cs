using System.Text.Json;
using System.Text.Json.Nodes;
using PipelineForge.Core.Model;

namespace PipelineForge.Core.Presets;

/// <summary>
/// Presets are partial configs stored as JSON. They are applied with JSON Merge Patch
/// (RFC 7386) semantics; locked paths are re-applied after the user's edits so a team
/// standard (e.g. "compliance.profile") cannot be switched off.
/// </summary>
public static class PresetMerger
{
    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = false };

    public static PipelineConfig Apply(PipelineConfig baseConfig, string presetJson)
    {
        var target = JsonSerializer.SerializeToNode(baseConfig, JsonDefaults.Options)!;
        var patch = Parse(presetJson);
        var merged = MergePatch(target, patch);
        return merged.Deserialize<PipelineConfig>(JsonDefaults.Options)!;
    }

    /// <summary>Forces each locked path (dot separated, camelCase) to the preset's value.</summary>
    public static PipelineConfig EnforceLocks(PipelineConfig config, string presetJson, IEnumerable<string> lockedPaths)
    {
        var target = JsonSerializer.SerializeToNode(config, JsonDefaults.Options)!.AsObject();
        var preset = Parse(presetJson);
        foreach (var path in lockedPaths)
        {
            var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) continue;
            var source = Navigate(preset, segments);
            if (source is null) continue;
            var parent = target;
            foreach (var seg in segments[..^1])
            {
                if (parent[seg] is not JsonObject next)
                {
                    next = new JsonObject();
                    parent[seg] = next;
                }
                parent = next;
            }
            parent[segments[^1]] = source.DeepClone();
        }
        return target.Deserialize<PipelineConfig>(JsonDefaults.Options)!;
    }

    /// <summary>Paths that are locked but whose value differs from the preset.</summary>
    public static IReadOnlyList<string> Violations(PipelineConfig config, string presetJson, IEnumerable<string> lockedPaths)
    {
        var current = JsonSerializer.SerializeToNode(config, JsonDefaults.Options)!;
        var preset = Parse(presetJson);
        return lockedPaths
            .Where(p =>
            {
                var segs = p.Split('.', StringSplitOptions.RemoveEmptyEntries);
                var want = Navigate(preset, segs);
                return want is not null && !JsonNode.DeepEquals(want, Navigate(current, segs));
            })
            .ToList();
    }

    public static JsonNode Parse(string json)
    {
        var node = JsonNode.Parse(json, NodeOptions, new JsonDocumentOptions { MaxDepth = 32 });
        return node as JsonObject ?? throw new JsonException("A preset must be a JSON object.");
    }

    private static JsonNode? Navigate(JsonNode? node, IEnumerable<string> segments)
    {
        foreach (var seg in segments)
        {
            if (node is not JsonObject obj || !obj.TryGetPropertyValue(seg, out node)) return null;
        }
        return node;
    }

    public static JsonNode MergePatch(JsonNode target, JsonNode patch)
    {
        if (patch is not JsonObject patchObj) return patch.DeepClone();
        var result = target as JsonObject ?? new JsonObject();
        foreach (var (key, value) in patchObj)
        {
            if (value is null) result.Remove(key);
            else if (result[key] is { } existing && value is JsonObject) result[key] = MergePatch(existing.DeepClone(), value);
            else result[key] = value.DeepClone();
        }
        return result;
    }
}
