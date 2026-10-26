using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using PipelineForge.Core.Scm;
using PipelineForge.Infrastructure.Net;
using PipelineForge.Infrastructure.Scm;

namespace PipelineForge.Infrastructure.Jenkins;

public sealed record JenkinsConnectionInfo(Uri BaseUrl, string Username, string ApiToken);

public sealed record JenkinsValidation(bool Valid, string Output);

public sealed record MultibranchJob(string Name, ScmKind Scm, string Repository, string ScmCredentialId, string? ServerUrl);

/// <summary>
/// Jenkins REST calls authenticated with a user API token (no CSRF crumb needed).
/// Job names are allow-listed and job XML is built with XElement, so values are escaped.
/// </summary>
public sealed partial class JenkinsClient(IHttpClientFactory factory, IOptions<OutboundOptions> outbound)
{
    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,100}$")] private static partial Regex JobNameRx();

    private const int MaxResponseBytes = 1_000_000;

    private async Task<HttpResponseMessage> SendAsync(JenkinsConnectionInfo conn, HttpMethod method, string relative, HttpContent? content, CancellationToken ct)
    {
        var uri = new Uri($"{conn.BaseUrl.ToString().TrimEnd('/')}/{relative}");
        OutboundGuard.ValidateUrl(uri, outbound.Value);
        var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{conn.Username}:{conn.ApiToken}")));
        return await factory.CreateClient(ScmHttp.ClientName).SendAsync(request, ct);
    }

    private static async Task<string> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        return body.Length > MaxResponseBytes ? body[..MaxResponseBytes] : body;
    }

    public async Task<string> WhoAmIAsync(JenkinsConnectionInfo conn, CancellationToken ct)
    {
        using var response = await SendAsync(conn, HttpMethod.Get, "me/api/json?tree=fullName", null, ct);
        if (!response.IsSuccessStatusCode) throw new ScmException(response.StatusCode, $"Jenkins returned HTTP {(int)response.StatusCode}.");
        return conn.Username;
    }

    /// <summary>Declarative linter. Only checks the Jenkinsfile itself, not loaded stage files.</summary>
    public async Task<JenkinsValidation> ValidateAsync(JenkinsConnectionInfo conn, string jenkinsfile, CancellationToken ct)
    {
        using var form = new FormUrlEncodedContent([new("jenkinsfile", jenkinsfile)]);
        using var response = await SendAsync(conn, HttpMethod.Post, "pipeline-model-converter/validate", form, ct);
        var text = await ReadAsync(response, ct);
        if (!response.IsSuccessStatusCode) return new(false, $"Jenkins returned HTTP {(int)response.StatusCode}.");
        return new(text.Contains("successfully validated", StringComparison.OrdinalIgnoreCase), text.Trim());
    }

    public async Task<string> UpsertMultibranchJobAsync(JenkinsConnectionInfo conn, MultibranchJob job, CancellationToken ct)
    {
        if (!JobNameRx().IsMatch(job.Name)) throw new ArgumentException("Invalid Jenkins job name.");
        var xml = JobXml(job);
        var name = Uri.EscapeDataString(job.Name);
        using (var exists = await SendAsync(conn, HttpMethod.Get, $"job/{name}/config.xml", null, ct))
        {
            var content = new StringContent(xml, Encoding.UTF8, "application/xml");
            var path = exists.IsSuccessStatusCode ? $"job/{name}/config.xml" : $"createItem?name={name}";
            using var response = await SendAsync(conn, HttpMethod.Post, path, content, ct);
            if (!response.IsSuccessStatusCode)
                throw new ScmException(response.StatusCode, $"Jenkins refused the job (HTTP {(int)response.StatusCode}). Are the branch source plugins installed?");
        }
        using (var scan = await SendAsync(conn, HttpMethod.Post, $"job/{name}/build?delay=0", null, ct))
        {
            // Starting the branch indexing is best effort.
        }
        return $"{conn.BaseUrl.ToString().TrimEnd('/')}/job/{name}/";
    }

    public static string JobXml(MultibranchJob job)
    {
        var parts = job.Repository.Split('/');
        if (parts.Length != 2) throw new ArgumentException("Repository must look like owner/name.");
        const string project = "org.jenkinsci.plugins.workflow.multibranch.WorkflowMultiBranchProject";
        XElement source;
        if (job.Scm == ScmKind.GitHub)
        {
            const string ns = "org.jenkinsci.plugins.github__branch__source";
            source = new XElement("source",
                new XAttribute("class", "org.jenkinsci.plugins.github_branch_source.GitHubSCMSource"),
                new XElement("id", $"pipelineforge-{Guid.NewGuid():N}"),
                job.ServerUrl is null ? null : new XElement("apiUri", job.ServerUrl),
                new XElement("credentialsId", job.ScmCredentialId),
                new XElement("repoOwner", parts[0]),
                new XElement("repository", parts[1]),
                new XElement("traits",
                    new XElement($"{ns}.BranchDiscoveryTrait", new XElement("strategyId", 1)),
                    new XElement($"{ns}.OriginPullRequestDiscoveryTrait", new XElement("strategyId", 1))));
        }
        else
        {
            const string ns = "com.cloudbees.jenkins.plugins.bitbucket";
            source = new XElement("source",
                new XAttribute("class", $"{ns}.BitbucketSCMSource"),
                new XElement("id", $"pipelineforge-{Guid.NewGuid():N}"),
                new XElement("serverUrl", job.ServerUrl ?? "https://bitbucket.org"),
                new XElement("credentialsId", job.ScmCredentialId),
                new XElement("repoOwner", parts[0]),
                new XElement("repository", parts[1]),
                new XElement("traits",
                    new XElement($"{ns}.BranchDiscoveryTrait", new XElement("strategyId", 1)),
                    new XElement($"{ns}.OriginPullRequestDiscoveryTrait", new XElement("strategyId", 1))));
        }

        var doc = new XDocument(
            new XElement(project,
                new XElement("description", "Managed by PipelineForge."),
                new XElement("orphanedItemStrategy",
                    new XAttribute("class", "com.cloudbees.hudson.plugins.folder.computed.DefaultOrphanedItemStrategy"),
                    new XElement("pruneDeadBranches", true),
                    new XElement("daysToKeep", -1),
                    new XElement("numToKeep", -1)),
                new XElement("sources",
                    new XAttribute("class", "jenkins.branch.MultiBranchProject$BranchSourceList"),
                    new XElement("data",
                        new XElement("jenkins.branch.BranchSource",
                            source,
                            new XElement("strategy",
                                new XAttribute("class", "jenkins.branch.DefaultBranchPropertyStrategy"),
                                new XElement("properties", new XAttribute("class", "empty-list"))))),
                    new XElement("owner", new XAttribute("class", project), new XAttribute("reference", "../.."))),
                new XElement("factory",
                    new XAttribute("class", "org.jenkinsci.plugins.workflow.multibranch.WorkflowBranchProjectFactory"),
                    new XElement("owner", new XAttribute("class", project), new XAttribute("reference", "../..")),
                    new XElement("scriptPath", "Jenkinsfile"))));
        return doc.Declaration + doc.ToString();
    }
}
