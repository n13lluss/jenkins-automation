using System.Net;
using PipelineForge.Infrastructure.Jenkins;
using PipelineForge.Infrastructure.Net;

namespace PipelineForge.Tests.Core;

public class OutboundGuardTests
{
    [Theory]
    [InlineData("169.254.169.254", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("::ffff:10.0.0.1", true)]
    [InlineData("fd12::1", true)]
    [InlineData("140.82.112.3", false)]
    public void Private_and_metadata_addresses_are_blocked(string ip, bool blocked) =>
        Assert.Equal(blocked, OutboundGuard.IsBlocked(IPAddress.Parse(ip), new OutboundOptions(), listed: false));

    [Fact]
    public void Metadata_stays_blocked_even_when_private_networks_are_allowed() =>
        Assert.True(OutboundGuard.IsBlocked(IPAddress.Parse("169.254.169.254"), new OutboundOptions { AllowPrivateNetworks = true }, listed: true));

    [Fact]
    public void Allow_list_restricts_hosts()
    {
        var o = new OutboundOptions { AllowedHosts = ["bitbucket.corp.example"] };
        OutboundGuard.ValidateUrl(new Uri("https://bitbucket.corp.example/rest"), o);
        Assert.Throws<OutboundBlockedException>(() => OutboundGuard.ValidateUrl(new Uri("https://evil.example"), o));
        Assert.Throws<OutboundBlockedException>(() => OutboundGuard.ValidateUrl(new Uri("https://user:pw@bitbucket.corp.example"), o));
    }

    [Fact]
    public void Jenkins_job_xml_escapes_values()
    {
        var xml = JenkinsClient.JobXml(new MultibranchJob("orders", PipelineForge.Core.Scm.ScmKind.BitbucketCloud, "acme/orders", "bb-cred</credentialsId><x>", null));
        Assert.Contains("bb-cred&lt;/credentialsId&gt;&lt;x&gt;", xml);
        Assert.Contains("BitbucketSCMSource", xml);
    }
}
