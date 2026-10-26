using PipelineForge.Core.Generation;

namespace PipelineForge.Tests.Generation;

/// <summary>Writes the sample pipelines to PF_SAMPLES_DIR for scripts/validate-jenkinsfiles.sh. No-op otherwise.</summary>
public class ExportSamples
{
    [Fact]
    public void Export()
    {
        var dir = Environment.GetEnvironmentVariable("PF_SAMPLES_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        foreach (var (name, config) in new[] { ("maven-helm-k8s", Samples.MavenHelm()), ("monorepo-docker-iso", Samples.Monorepo()) })
        {
            var result = PipelineGenerator.Generate(config);
            Assert.True(result.Success);
            foreach (var f in result.Files)
            {
                var path = Path.Combine(dir, name, f.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, f.Content);
            }
        }
    }
}
