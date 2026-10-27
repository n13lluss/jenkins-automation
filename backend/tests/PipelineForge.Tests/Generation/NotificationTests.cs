using PipelineForge.Core.Generation;
using PipelineForge.Core.Model;

namespace PipelineForge.Tests.Generation;

public class NotificationTests
{
    public static PipelineConfig Notifying(AgentType agent = AgentType.Kubernetes, PipelineLayout layout = PipelineLayout.InRepo)
    {
        var c = WorkflowCoverageTests.SecureJava(agent);
        c.Layout = layout;
        c.Library.Repository = "acme/jenkins-library";
        c.Notifications = new()
        {
            Slack = new() { Enabled = true, Channel = "#orders-deploys" },
            Teams = new() { Enabled = true, WebhookCredentialId = "teams-orders" },
            Email = new() { Enabled = true, Recipients = ["team-orders@example.com", "security@example.com"] },
        };
        return c;
    }

    private static Dictionary<string, string> Files(PipelineConfig config)
    {
        var result = PipelineGenerator.Generate(config);
        Assert.True(result.Success, string.Join("\n", result.Errors.Select(e => $"{e.Path}: {e.Message}")));
        return result.Files.ToDictionary(f => f.Path, f => f.Content);
    }

    [Theory]
    [InlineData(AgentType.Kubernetes)]
    [InlineData(AgentType.Docker)]
    public void Every_channel_and_event_is_wired(AgentType agent)
    {
        var f = Files(Notifying(agent));
        var jf = f["Jenkinsfile"];
        Assert.Contains("def pfNotify(String event, String text) {", jf);
        Assert.Contains("slackSend channel: '#orders-deploys'", jf);
        Assert.Contains("credentialsId: 'teams-orders', variable: 'TEAMS_WEBHOOK'", jf);
        Assert.Contains("groovy.json.JsonOutput.toJson(card)", jf);
        Assert.Contains("emailext to: 'team-orders@example.com, security@example.com'", jf);
        Assert.Contains("pfNotify('failure', 'Build failed')", jf);
        Assert.Contains("pfNotify('fixed', 'Build is back to normal')", jf);
        Assert.Contains("pfNotify('approval', 'Waiting for approval to deploy to Production')", jf);
        Assert.Contains("pfNotify('deployed', 'Deployed orders to Production')", jf);
        Assert.DoesNotContain("Deployed orders to Test", jf);
        // The approval message goes out before the build blocks on input.
        Assert.True(jf.IndexOf("pfNotify('approval'", StringComparison.Ordinal) < jf.IndexOf("input message:", StringComparison.Ordinal));
        // The method is defined outside the pipeline block so it works before Init.
        Assert.True(jf.IndexOf("def pfNotify", StringComparison.Ordinal) > jf.LastIndexOf("pfNotify('fixed'", StringComparison.Ordinal));
        Assert.Contains("pfNotify('failure'", f["Jenkinsfile.scheduled"]);
        Assert.Contains("def pfNotify", f["Jenkinsfile.scheduled"]);
        Assert.Contains("http_request (Teams Workflows webhook)", f["docs/PIPELINE.md"]);
        Assert.Contains(PipelineGenerator.Generate(Notifying(agent)).Credentials, cr => cr.Id == "teams-orders");
    }

    [Fact]
    public void Library_var_defines_the_method_next_to_call()
    {
        var f = Files(Notifying(layout: PipelineLayout.SharedLibrary));
        var lib = f["library/vars/pfOrders.groovy"];
        Assert.Contains("def pfNotify(String event, String text) {", lib);
        Assert.True(lib.IndexOf("def call() {", StringComparison.Ordinal) < lib.IndexOf("def pfNotify", StringComparison.Ordinal));
        Assert.DoesNotContain("pfNotify", f["Jenkinsfile"]);
    }

    [Fact]
    public void Events_can_be_limited()
    {
        var c = Notifying();
        c.Notifications.Events = [NotifyEvent.Failure];
        var jf = Files(c)["Jenkinsfile"];
        Assert.Contains("pfNotify('failure'", jf);
        Assert.DoesNotContain("pfNotify('approval'", jf);
        Assert.DoesNotContain("pfNotify('deployed'", jf);
        Assert.DoesNotContain("fixed {", jf);
    }

    [Fact]
    public void Nothing_without_a_channel()
    {
        var jf = Files(WorkflowCoverageTests.SecureJava())["Jenkinsfile"];
        Assert.DoesNotContain("pfNotify", jf);
    }

    [Fact]
    public void Groovy_stays_balanced()
    {
        foreach (var agent in new[] { AgentType.Kubernetes, AgentType.Docker })
            foreach (var layout in new[] { PipelineLayout.InRepo, PipelineLayout.SharedLibrary })
                foreach (var (path, content) in Files(Notifying(agent, layout)).Where(kv => kv.Key.EndsWith(".groovy") || kv.Key.StartsWith("Jenkinsfile")))
                    Assert.True(PipelineForge.Core.Validation.ConfigValidator.BracesBalanced(content), $"{agent} {layout} {path}");
    }

    [Theory]
    [InlineData("channel", "#ops'); evil('")]
    [InlineData("email", "a@b.com, evil@x.com")]
    [InlineData("email", "x@y.com'\nsh 'id")]
    [InlineData("webhook", "teams webhook")]
    public void Injection_is_rejected(string field, string value)
    {
        var c = Notifying();
        string path;
        switch (field)
        {
            case "channel": c.Notifications.Slack.Channel = value; path = "notifications.slack.channel"; break;
            case "email": c.Notifications.Email.Recipients = [value]; path = "notifications.email.recipients"; break;
            default: c.Notifications.Teams.WebhookCredentialId = value; path = "notifications.teams.webhookCredentialId"; break;
        }
        Assert.Contains(PipelineGenerator.Generate(c).Errors, e => e.Path == path);
    }

    [Fact]
    public void Iso_profile_warns_without_a_channel_and_adds_failure_events()
    {
        var c = Samples.MavenHelm();
        c.Compliance.Profile = ComplianceProfile.Iso27001;
        Assert.Contains(PipelineGenerator.Generate(c).Compliance.Warnings, w => w.StartsWith("A.8.16"));

        c.Notifications.Slack.Enabled = true;
        c.Notifications.Events = [NotifyEvent.Fixed];
        var effective = PipelineGenerator.Generate(c).EffectiveConfig.Notifications.Events;
        Assert.Contains(NotifyEvent.Failure, effective);
        Assert.Contains(NotifyEvent.ProductionDeployed, effective);
    }
}
