using PipelineForge.Core.Generation;
using PipelineForge.Core.Metrics;
using PipelineForge.Core.Model;

namespace PipelineForge.Tests.Core;

public class DeliveryMetricsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static DeployRecord Deploy(string env, double hoursAgo, bool ok, double? leadHours = null) =>
        new(env, leadHours is { } l ? Now.AddHours(-hoursAgo - l) : null, Now.AddHours(-hoursAgo), ok);

    [Fact]
    public void Computes_the_four_measures_per_environment()
    {
        var events = new[]
        {
            Deploy("prod", 200, ok: true, leadHours: 2),
            Deploy("prod", 100, ok: false),
            Deploy("prod", 97, ok: true, leadHours: 4),   // restored after 3 hours
            Deploy("prod", 10, ok: true, leadHours: 6),
            Deploy("test", 5, ok: true, leadHours: 1),
            Deploy("prod", 24 * 40, ok: true),            // outside the window
        };
        var result = DeliveryMetrics.Compute(events, Now, 28);

        var prod = result.Single(r => r.Environment == "prod");
        Assert.Equal(4, prod.Deploys);
        Assert.Equal(1, prod.Failures);
        Assert.Equal(0.75, prod.DeploysPerWeek);
        Assert.Equal(0.25, prod.ChangeFailureRate);
        Assert.Equal(4, prod.MedianLeadTimeHours);
        Assert.Equal(3, prod.MedianTimeToRestoreHours);
        Assert.Equal(28, prod.Daily.Count);
        Assert.Equal(4, prod.Daily.Sum(d => d.Successes + d.Failures));
        Assert.Equal(new[] { "prod", "test" }, result.Select(r => r.Environment));
    }

    [Fact]
    public void Unrestored_failures_have_no_time_to_restore()
    {
        var prod = DeliveryMetrics.Compute([Deploy("prod", 5, ok: false)], Now, 7).Single();
        Assert.Null(prod.MedianTimeToRestoreHours);
        Assert.Null(prod.MedianLeadTimeHours);
        Assert.Equal(1, prod.ChangeFailureRate);
    }

    [Fact]
    public void Pipeline_reports_each_deploy_without_exposing_the_token()
    {
        var c = Tests.Generation.WorkflowCoverageTests.SecureJava();
        c.Metrics = new() { Enabled = true, Url = "https://pipelineforge.example.com/", TokenCredentialId = "pf-ingest" };
        var result = PipelineGenerator.Generate(c);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        var deploy = result.Files.Single(f => f.Path == "jenkins/stages/deploy.groovy").Content;
        Assert.Contains("reportDeployment(common, cfg, 'success', startedAt)", deploy);
        Assert.Contains("reportDeployment(common, cfg, 'failure', startedAt)", deploy);
        Assert.Contains("'PF_INGEST_URL=https://pipelineforge.example.com/api/ingest/deployments'", deploy);
        Assert.Contains("-H @\"$headers\"", deploy);
        Assert.DoesNotContain("-H \"Authorization", deploy);
        Assert.Contains(result.Credentials, cr => cr.Id == "pf-ingest");
        Assert.True(PipelineForge.Core.Validation.ConfigValidator.BracesBalanced(deploy));
    }

    [Theory]
    [InlineData("http://pipelineforge.example.com")]
    [InlineData("https://x.example.com/'; curl evil")]
    [InlineData("https://x.example.com/?a=b")]
    [InlineData("ftp://x.example.com")]
    public void Metrics_url_is_validated(string url)
    {
        var c = Tests.Generation.WorkflowCoverageTests.SecureJava();
        c.Metrics = new() { Enabled = true, Url = url };
        Assert.Contains(PipelineGenerator.Generate(c).Errors, e => e.Path == "metrics.url");
    }

    [Fact]
    public void Local_http_is_allowed_for_the_compose_stack()
    {
        var c = Tests.Generation.WorkflowCoverageTests.SecureJava();
        c.Metrics = new() { Enabled = true, Url = "http://frontend:3000" };
        Assert.DoesNotContain(PipelineGenerator.Generate(c).Errors, e => e.Path == "metrics.url");
    }
}
