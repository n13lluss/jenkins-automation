using System.Text;

namespace PipelineForge.Core.Drift;

public enum DriftStatus { Missing, Identical, Changed, Customised, Unavailable }

public sealed record FileDrift(string Path, DriftStatus Status, string? RepositoryVersion, string? Diff, string? Note = null);

/// <summary>Compares generated files with the copies in the repository and renders unified diffs.</summary>
public static class DriftComparer
{
    /// <summary>Above this many lines per side the diff is not computed (the file is still marked changed).</summary>
    public const int MaxDiffLines = 4000;

    public static FileDrift Compare(string path, string generated, string? repository)
    {
        if (repository is null) return new(path, DriftStatus.Missing, null, null);
        var a = VersionMarker.Normalise(repository);
        var b = VersionMarker.Normalise(generated);
        var version = VersionMarker.Read(repository);
        if (a == b) return new(path, DriftStatus.Identical, version, null);
        // Without our marker the file was written by hand, or the marker was removed while editing it.
        var status = version is null ? DriftStatus.Customised : DriftStatus.Changed;
        return new(path, status, version, UnifiedDiff(path, a, b));
    }

    /// <summary>Line diff (longest common subsequence) with three lines of context, in unified format.</summary>
    public static string? UnifiedDiff(string path, string before, string after, int context = 3)
    {
        var x = before.TrimEnd('\n').Split('\n');
        var y = after.TrimEnd('\n').Split('\n');
        if (x.Length > MaxDiffLines || y.Length > MaxDiffLines) return null;

        var lcs = new int[x.Length + 1, y.Length + 1];
        for (var i = x.Length - 1; i >= 0; i--)
            for (var j = y.Length - 1; j >= 0; j--)
                lcs[i, j] = x[i] == y[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        // Edit script: ' ' keep, '-' remove (repository), '+' add (generated).
        var ops = new List<(char Op, string Line, int A, int B)>();
        int p = 0, q = 0;
        while (p < x.Length || q < y.Length)
        {
            if (p < x.Length && q < y.Length && x[p] == y[q]) { ops.Add((' ', x[p], p, q)); p++; q++; }
            else if (q < y.Length && (p == x.Length || lcs[p, q + 1] >= lcs[p + 1, q])) { ops.Add(('+', y[q], p, q)); q++; }
            else { ops.Add(('-', x[p], p, q)); p++; }
        }

        var sb = new StringBuilder().Append("--- a/").Append(path).Append("\n+++ b/").Append(path).Append('\n');
        var k = 0;
        while (k < ops.Count)
        {
            if (ops[k].Op == ' ') { k++; continue; }
            var start = Math.Max(0, k - context);
            var end = k;
            // Extend the hunk while changes are within 2 * context lines of each other.
            while (end < ops.Count)
            {
                if (ops[end].Op != ' ') { end++; continue; }
                var next = end;
                while (next < ops.Count && ops[next].Op == ' ') next++;
                if (next < ops.Count && next - end <= 2 * context) { end = next; continue; }
                end = Math.Min(ops.Count, end + context);
                break;
            }
            var hunk = ops.GetRange(start, end - start);
            var aCount = hunk.Count(o => o.Op != '+');
            var bCount = hunk.Count(o => o.Op != '-');
            sb.Append($"@@ -{hunk[0].A + (aCount == 0 ? 0 : 1)},{aCount} +{hunk[0].B + (bCount == 0 ? 0 : 1)},{bCount} @@\n");
            foreach (var o in hunk) sb.Append(o.Op).Append(o.Line).Append('\n');
            k = end;
        }
        return sb.ToString();
    }
}
