using System.Diagnostics;
using PipelineForge.Core.Drift;
using PipelineForge.Core.Generation;

namespace PipelineForge.Tests.Core;

public class DriftTests
{
    [Fact]
    public void Generated_files_carry_the_version_marker()
    {
        var files = PipelineGenerator.Generate(Samples.MavenHelm()).Files.ToDictionary(f => f.Path, f => f.Content);
        Assert.StartsWith($"// {VersionMarker.Key} ", files["Jenkinsfile"]);
        Assert.StartsWith($"# {VersionMarker.Key} ", files["jenkins/config/environments.yaml"]);
        Assert.StartsWith($"<!-- {VersionMarker.Key} ", files["docs/PIPELINE.md"]);
        Assert.Equal(VersionMarker.Version, VersionMarker.Read(files["Jenkinsfile"]));
        var py = PipelineGenerator.Generate(Tests.Generation.WorkflowCoverageTests.SecureJava()).Files.First(f => f.Path.EndsWith(".py")).Content;
        Assert.StartsWith("#!/usr/bin/env python3\n# " + VersionMarker.Key, py);
    }

    [Fact]
    public void Statuses_follow_the_repository_copy()
    {
        const string generated = "// pipelineforge-version: 2.0.0\npipeline {\n    agent none\n}\n";
        Assert.Equal(DriftStatus.Missing, DriftComparer.Compare("Jenkinsfile", generated, null).Status);
        // Only the version differs: identical.
        var same = DriftComparer.Compare("Jenkinsfile", generated, "// pipelineforge-version: 1.0.0\r\npipeline {\r\n    agent none\r\n}\r\n");
        Assert.Equal(DriftStatus.Identical, same.Status);
        Assert.Equal("1.0.0", same.RepositoryVersion);
        Assert.Equal(DriftStatus.Changed, DriftComparer.Compare("Jenkinsfile", generated, "// pipelineforge-version: 1.0.0\npipeline {\n    agent any\n}\n").Status);
        Assert.Equal(DriftStatus.Customised, DriftComparer.Compare("Jenkinsfile", generated, "pipeline {\n    agent any\n}\n").Status);
    }

    [Theory]
    [InlineData("a\nb\nc\n", "a\nB\nc\n")]
    [InlineData("", "new\nfile\n")]
    [InlineData("1\n2\n3\n4\n5\n6\n7\n8\n9\n10\n11\n12\n13\n14\n15\n", "0\n1\n2\n3\n4\n5\n6\n7\n8\nnine\n10\n11\n12\n13\n14\n")]
    [InlineData("x\ny\n", "")]
    public void Unified_diff_applies_with_patch(string before, string after)
    {
        var diff = DriftComparer.UnifiedDiff("file.txt", before == "" ? "\n" : before, after == "" ? "\n" : after)!;
        var dir = Directory.CreateTempSubdirectory("pf-diff-").FullName;
        File.WriteAllText(Path.Combine(dir, "file.txt"), before == "" ? "\n" : before);
        File.WriteAllText(Path.Combine(dir, "change.diff"), diff);
        var psi = new ProcessStartInfo("patch", "-p1 -i change.diff") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, output + "\n" + diff);
        Assert.Equal(after == "" ? "\n" : after, File.ReadAllText(Path.Combine(dir, "file.txt")));
    }
}
