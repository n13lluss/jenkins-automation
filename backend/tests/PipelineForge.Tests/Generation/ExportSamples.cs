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
        foreach (var (name, config) in new[]
                 {
                     ("maven-helm-k8s", Samples.MavenHelm()),
                     ("monorepo-docker-iso", Samples.Monorepo()),
                     ("secure-java-k8s", WorkflowCoverageTests.SecureJava()),
                     ("secure-java-docker", WorkflowCoverageTests.SecureJava(PipelineForge.Core.Model.AgentType.Docker)),
                     ("promote-k8s", SupplyChainTests.Promoting()),
                     ("promote-docker", SupplyChainTests.Promoting(PipelineForge.Core.Model.AgentType.Docker)),
                     ("library-k8s", SharedLibraryTests.Library()),
                     ("library-docker", SharedLibraryTests.Library(PipelineForge.Core.Model.AgentType.Docker)),
                     ("safe-k8s-flyway", DeploySafetyTests.Safe()),
                     ("exceptions-docker", ExceptionRegisterTests.Register(PipelineForge.Core.Model.AgentType.Docker)),
                     ("safe-docker-liquibase", DeploySafetyTests.Safe(PipelineForge.Core.Model.AgentType.Docker, PipelineForge.Core.Model.MigrationTool.Liquibase)),
                 })
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
