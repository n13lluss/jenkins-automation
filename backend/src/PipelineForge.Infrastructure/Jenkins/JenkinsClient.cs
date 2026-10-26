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

public sealed record MultibranchJob(string Name, ScmKind Scm, string Repository, string ScmCredentialId, string? ServerUrl, string ScriptPath = "Jenkinsfile")
{
    /// <summary>Folder library to register on the job (shared library layout).</summary>
    public JobLibrary? Library { get; init; }
}

/// <summary>A shared library loaded from <paramref name="Repository"/> on the same SCM; the version cannot be overridden.</summary>
public sealed record JobLibrary(string Name, string Repository, string DefaultVersion);

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
        const string project = "org.jenkinsci.plugins.workflow.multibranch.WorkflowMultiBranchProject";
        // Fork pull requests are built, but only forks of trusted contributors use their own Jenkinsfile;
        // the others run the target branch's Jenkinsfile.
        var source = Source(job, job.Repository, $"pipelineforge-{Guid.NewGuid():N}", forks: true);

        var doc = new XDocument(
            new XElement(project,
                new XElement("description", "Managed by PipelineForge."),
                job.Library is { } lib ? new XElement("properties", FolderLibrary(job, lib)) : null,
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
                    new XElement("scriptPath", job.ScriptPath is "Jenkinsfile" or "Jenkinsfile.scheduled" ? job.ScriptPath : throw new ArgumentException("Unsupported script path.")))));
        return doc.Declaration + doc.ToString();
    }

    private static (string Owner, string Repo) Split(string repository)
    {
        var parts = repository.Split('/');
        if (parts.Length != 2 || parts.Any(p => p.Length == 0)) throw new ArgumentException("Repository must look like owner/name.");
        return (parts[0], parts[1]);
    }

    private static XElement Source(MultibranchJob job, string repository, string id, bool forks)
    {
        var (owner, repo) = Split(repository);
        if (job.Scm == ScmKind.GitHub)
        {
            const string ns = "org.jenkinsci.plugins.github__branch__source";
            return new XElement("source",
                new XAttribute("class", "org.jenkinsci.plugins.github_branch_source.GitHubSCMSource"),
                new XElement("id", id),
                job.ServerUrl is null ? null : new XElement("apiUri", job.ServerUrl),
                new XElement("credentialsId", job.ScmCredentialId),
                new XElement("repoOwner", owner),
                new XElement("repository", repo),
                new XElement("traits",
                    new XElement($"{ns}.BranchDiscoveryTrait", new XElement("strategyId", 1)),
                    forks ? new XElement($"{ns}.OriginPullRequestDiscoveryTrait", new XElement("strategyId", 1)) : null,
                    forks ? new XElement($"{ns}.ForkPullRequestDiscoveryTrait",
                        new XElement("strategyId", 1),
                        new XElement("trust", new XAttribute("class", "org.jenkinsci.plugins.github_branch_source.ForkPullRequestDiscoveryTrait$TrustPermission"))) : null));
        }
        const string bb = "com.cloudbees.jenkins.plugins.bitbucket";
        return new XElement("source",
            new XAttribute("class", $"{bb}.BitbucketSCMSource"),
            new XElement("id", id),
            new XElement("serverUrl", job.ServerUrl ?? "https://bitbucket.org"),
            new XElement("credentialsId", job.ScmCredentialId),
            new XElement("repoOwner", owner),
            new XElement("repository", repo),
            new XElement("traits",
                new XElement($"{bb}.BranchDiscoveryTrait", new XElement("strategyId", 1)),
                forks ? new XElement($"{bb}.OriginPullRequestDiscoveryTrait", new XElement("strategyId", 1)) : null,
                forks ? new XElement($"{bb}.ForkPullRequestDiscoveryTrait",
                    new XElement("strategyId", 1),
                    new XElement("trust", new XAttribute("class", $"{bb}.ForkPullRequestDiscoveryTrait$TrustTeamForks"))) : null));
    }

    private static XElement LibrarySource(MultibranchJob job, JobLibrary lib)
    {
        var scm = Source(job, lib.Repository, $"pipelineforge-lib-{Guid.NewGuid():N}", forks: false);
        scm.Name = "scm";
        return scm;
    }

    private static XElement FolderLibrary(MultibranchJob job, JobLibrary lib)
    {
        const string libs = "org.jenkinsci.plugins.workflow.libs";
        return new XElement($"{libs}.FolderLibraries",
            new XElement("libraries",
                new XElement($"{libs}.LibraryConfiguration",
                    new XElement("name", lib.Name),
                    new XElement("retriever",
                        new XAttribute("class", $"{libs}.SCMSourceRetriever"),
                        new XElement("clone", false),
                        LibrarySource(job, lib)),
                    new XElement("defaultVersion", lib.DefaultVersion),
                    new XElement("implicit", false),
                    new XElement("allowVersionOverride", false),
                    new XElement("includeInChangesets", false))));
    }
}
