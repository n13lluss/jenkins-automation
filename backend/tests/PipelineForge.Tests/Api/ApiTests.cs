using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PipelineForge.Core;
using PipelineForge.Infrastructure.Persistence;

namespace PipelineForge.Tests.Api;

public class ApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private static readonly JsonSerializerOptions Json = JsonDefaults.Create();

    public ApiTests(ApiFactory f) => _f = f;

    private static async Task<JsonElement> Read(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private async Task<Guid> Workspace(HttpClient c, string name)
    {
        var ws = await Read(await c.PostAsJsonAsync("/api/workspaces", new { name }));
        return ws.GetProperty("id").GetGuid();
    }

    private async Task Member(Guid ws, string adminUser, string user, string role)
    {
        await _f.As(user).GetAsync("/api/me");
        var r = await _f.As(adminUser).PutAsJsonAsync($"/api/workspaces/{ws}/members", new { email = $"{user}@example.com", role });
        await Read(r);
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        var r = await _f.CreateClient().GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Creator_is_admin_and_roles_are_enforced()
    {
        var admin = _f.As("ada");
        var ws = await Workspace(admin, "Team A");
        await Member(ws, "ada", "vic", "viewer");

        var connection = new { kind = "scm", name = "GitHub", provider = "GitHub", baseUrl = "https://api.github.com", secret = "ghp_secret_value" };
        var denied = await _f.As("vic").PostAsJsonAsync($"/api/workspaces/{ws}/connections", connection);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var created = await admin.PostAsJsonAsync($"/api/workspaces/{ws}/connections", connection);
        var body = await created.Content.ReadAsStringAsync();
        Assert.True(created.IsSuccessStatusCode, body);
        Assert.DoesNotContain("ghp_secret_value", body);
        Assert.DoesNotContain("secretRef", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ghp_secret_value", _f.Secrets.Values.Values);

        var list = await _f.As("vic").GetStringAsync($"/api/workspaces/{ws}/connections");
        Assert.DoesNotContain("ghp_secret_value", list);
    }

    [Fact]
    public async Task Members_of_one_workspace_cannot_reach_another()
    {
        var wsA = await Workspace(_f.As("alice"), "A");
        var wsB = await Workspace(_f.As("bob"), "B");
        var project = await Read(await _f.As("bob").PostAsJsonAsync($"/api/workspaces/{wsB}/pipelines",
            new { name = "secret-app", source = "blank", config = Samples.MavenHelm() }, Json));
        var id = project.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await _f.As("alice").GetAsync($"/api/workspaces/{wsB}/pipelines/{id}")).StatusCode);
        // Using her own workspace id with bob's project id must not find it either.
        Assert.Equal(HttpStatusCode.NotFound, (await _f.As("alice").GetAsync($"/api/workspaces/{wsA}/pipelines/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _f.As("alice").GetAsync($"/api/workspaces/{wsB}/audit")).StatusCode);
    }

    [Fact]
    public async Task Preview_generates_and_reports_validation_errors()
    {
        var c = _f.As("pat");
        var ws = await Workspace(c, "Preview");
        var ok = await Read(await c.PostAsJsonAsync($"/api/workspaces/{ws}/pipelines/preview", new { config = Samples.Monorepo(), applyPreset = false }, Json));
        Assert.True(ok.GetProperty("success").GetBoolean());
        Assert.Contains(ok.GetProperty("files").EnumerateArray(), f => f.GetProperty("path").GetString() == "Jenkinsfile");

        var bad = Samples.MavenHelm();
        bad.Environments[0].BranchPattern = "main'; evil";
        var errors = await Read(await c.PostAsJsonAsync($"/api/workspaces/{ws}/pipelines/preview", new { config = bad, applyPreset = false }, Json));
        Assert.False(errors.GetProperty("success").GetBoolean());
        Assert.Empty(errors.GetProperty("files").EnumerateArray());
    }

    [Fact]
    public async Task Only_admins_can_lock_preset_settings_and_locks_are_enforced_on_save()
    {
        var ws = await Workspace(_f.As("ann"), "Presets");
        await Member(ws, "ann", "max", "maintainer");
        var preset = new { name = "ISO standard", config = new { compliance = new { profile = "iso27001" } }, lockedPaths = new[] { "compliance.profile" } };

        Assert.Equal(HttpStatusCode.Forbidden, (await _f.As("max").PostAsJsonAsync($"/api/workspaces/{ws}/presets", preset)).StatusCode);
        var created = await Read(await _f.As("ann").PostAsJsonAsync($"/api/workspaces/{ws}/presets", preset));
        var presetId = created.GetProperty("id").GetGuid();

        var config = Samples.MavenHelm();
        config.Compliance.Profile = PipelineForge.Core.Model.ComplianceProfile.None;
        var project = await Read(await _f.As("max").PostAsJsonAsync($"/api/workspaces/{ws}/pipelines",
            new { name = "orders", source = "blank", config, presetId }, Json));
        Assert.Equal("iso27001", project.GetProperty("config").GetProperty("compliance").GetProperty("profile").GetString());
    }

    [Fact]
    public async Task Branch_protection_needs_a_maintainer_and_a_linked_repository()
    {
        var c = _f.As("bea");
        var ws = await Workspace(c, "Rules");
        await Member(ws, "bea", "vin", "viewer");
        var project = await Read(await c.PostAsJsonAsync($"/api/workspaces/{ws}/pipelines",
            new { name = "orders", source = "blank", config = Samples.MavenHelm() }, Json));
        var url = $"/api/workspaces/{ws}/pipelines/{project.GetProperty("id").GetGuid()}/branch-protection";

        Assert.Equal(HttpStatusCode.Forbidden, (await _f.As("vin").PostAsJsonAsync(url, new { approvals = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync(url, new { approvals = 1 })).StatusCode);
    }

    [Fact]
    public async Task Download_returns_a_zip_and_records_the_run()
    {
        var c = _f.As("dan");
        var ws = await Workspace(c, "Zip");
        var project = await Read(await c.PostAsJsonAsync($"/api/workspaces/{ws}/pipelines",
            new { name = "orders", source = "blank", config = Samples.MavenHelm() }, Json));
        var id = project.GetProperty("id").GetGuid();

        var r = await c.GetAsync($"/api/workspaces/{ws}/pipelines/{id}/download");
        Assert.Equal("application/zip", r.Content.Headers.ContentType?.MediaType);
        using var zip = new ZipArchive(await r.Content.ReadAsStreamAsync());
        Assert.Contains(zip.Entries, e => e.FullName == "Jenkinsfile");
        Assert.Contains(zip.Entries, e => e.FullName == "jenkins/stages/security.groovy");

        var runs = await Read(await c.GetAsync($"/api/workspaces/{ws}/pipelines/{id}/runs"));
        Assert.Single(runs.EnumerateArray());
        var audit = await Read(await c.GetAsync($"/api/workspaces/{ws}/audit"));
        Assert.Contains(audit.EnumerateArray(), a => a.GetProperty("action").GetString() == "pipeline.download");
    }

    [Fact]
    public async Task Built_in_preset_is_listed_and_applied_in_preview()
    {
        var c = _f.As("bea");
        var ws = await Workspace(c, "Built-in");
        var list = await Read(await c.GetAsync("/api/presets/built-in"));
        var id = list.EnumerateArray().Single().GetProperty("id").GetString();
        var preview = await Read(await c.PostAsJsonAsync($"/api/workspaces/{ws}/pipelines/preview",
            new { config = Samples.MavenHelm(), applyPreset = false, builtInPresetId = id }, Json));
        Assert.True(preview.GetProperty("success").GetBoolean(), preview.GetProperty("errors").ToString());
        Assert.Contains(preview.GetProperty("files").EnumerateArray(), f => f.GetProperty("path").GetString() == "Jenkinsfile.scheduled");
        Assert.True(preview.GetProperty("effectiveConfig").GetProperty("signing").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Ssrf_targets_are_refused_for_connections()
    {
        var c = _f.As("sam");
        var ws = await Workspace(c, "SSRF");
        foreach (var url in new[] { "http://169.254.169.254/latest", "https://127.0.0.1", "https://10.0.0.5", "http://example.com" })
        {
            var r = await c.PostAsJsonAsync($"/api/workspaces/{ws}/connections",
                new { kind = "jenkins", name = "J", provider = "jenkins", baseUrl = url, username = "u", secret = "t" });
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }
    }

    [Fact]
    public void Audit_log_is_append_only()
    {
        _f.Db(db =>
        {
            db.AuditEvents.Add(new AuditEvent { Action = "test" });
            db.SaveChanges();
            var e = db.AuditEvents.First();
            e.Action = "tampered";
            Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
            return 0;
        });
    }
}
