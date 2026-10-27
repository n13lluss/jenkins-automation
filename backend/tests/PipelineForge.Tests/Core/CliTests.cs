using System.Text.Json;
using PipelineForge.Core;

namespace PipelineForge.Tests.Core;

public class CliTests
{
    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = PipelineForge.Cli.Program.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string WriteConfig(object config)
    {
        var dir = Directory.CreateTempSubdirectory("pf-cli-").FullName;
        var path = Path.Combine(dir, "pipeline.json");
        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonDefaults.Options));
        return path;
    }

    [Fact]
    public void Generate_writes_the_files_and_refuses_to_overwrite()
    {
        var config = WriteConfig(Samples.MavenHelm());
        var outDir = Path.Combine(Path.GetDirectoryName(config)!, "out");
        var (code, stdout, stderr) = Run("generate", "--config", config, "--out", outDir);
        Assert.True(code == 0, stderr);
        Assert.Contains("wrote", stdout);
        Assert.True(File.Exists(Path.Combine(outDir, "Jenkinsfile")));
        Assert.True(File.Exists(Path.Combine(outDir, "jenkins/stages/deploy.groovy")));

        Assert.Equal(2, Run("generate", "--config", config, "--out", outDir).Code);
        Assert.Equal(0, Run("generate", "--config", config, "--out", outDir, "--force").Code);
    }

    [Fact]
    public void Validate_reports_errors_with_their_path()
    {
        var bad = Samples.MavenHelm();
        bad.Name = "Bad Name!";
        var (code, _, stderr) = Run("validate", "--config", WriteConfig(bad));
        Assert.Equal(1, code);
        Assert.Contains("error: name:", stderr);
        Assert.Equal(0, Run("validate", "--config", WriteConfig(Samples.MavenHelm())).Code);
    }

    [Fact]
    public void Presets_are_listed_and_applied()
    {
        Assert.Contains("builtin-secure-java", Run("presets").Out);
        var (code, stdout, stderr) = Run("validate", "--config", WriteConfig(Samples.MavenHelm()), "--preset", "builtin-secure-java");
        Assert.True(code == 0, stderr);
        Assert.Contains("valid:", stdout);
        Assert.Equal(2, Run("validate", "--config", WriteConfig(Samples.MavenHelm()), "--preset", "nope").Code);
    }

    [Theory]
    [InlineData("frobnicate")]
    [InlineData("generate", "--config")]
    [InlineData("generate", "--config", "/does/not/exist.json", "--out", "x")]
    public void Bad_usage_exits_with_2(params string[] args) => Assert.Equal(2, Run(args).Code);
}
