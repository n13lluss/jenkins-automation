using System.Diagnostics;
using PipelineForge.Core.Generation;
using PipelineForge.Core.Model;

namespace PipelineForge.Tests.Generation;

public class ExceptionRegisterTests
{
    public static PipelineConfig Register(AgentType agent = AgentType.Kubernetes)
    {
        var c = WorkflowCoverageTests.SecureJava(agent);
        c.Security.ExceptionRegister = true;
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
    public void Register_replaces_hand_edited_ignore_files(AgentType agent)
    {
        var f = Files(Register(agent));
        Assert.Contains("exceptions: []", f["security/exceptions.yaml"]);
        Assert.Contains("jenkins/scripts/exceptions.py", f.Keys);
        Assert.DoesNotContain("security/.trivyignore", f.Keys);
        Assert.DoesNotContain("security/dependency-check-suppressions.xml", f.Keys);

        var jf = f["Jenkinsfile"];
        Assert.Contains("stage('Exception review')", jf);
        Assert.True(jf.IndexOf("stage('Exception review')", StringComparison.Ordinal) < jf.IndexOf("stage('Dependency scan')", StringComparison.Ordinal));
        Assert.Contains("unstash 'pf-exceptions'", jf);
        Assert.Contains("stash name: 'pf-exceptions'", f["jenkins/stages/common.groovy"]);
        Assert.Contains("common.applyExceptions()", f["Jenkinsfile.scheduled"]);
        Assert.Contains("--policy-path=.snyk", f["jenkins/stages/security.groovy"]);
        if (agent == AgentType.Kubernetes) Assert.Contains("- name: python", f["jenkins/agents/deploy-pod.yaml"]);
    }

    [Fact]
    public void Off_by_default()
    {
        var f = Files(WorkflowCoverageTests.SecureJava());
        Assert.DoesNotContain("security/exceptions.yaml", f.Keys);
        Assert.DoesNotContain("pf-exceptions", f["Jenkinsfile"]);
        Assert.Contains("security/.trivyignore", f.Keys);
    }

    private static (int Code, string Output, string Dir) Run(string register, string today = "2026-10-27", string? zapRules = null)
    {
        var dir = Directory.CreateTempSubdirectory("pf-exceptions-").FullName;
        File.WriteAllText(Path.Combine(dir, "exceptions.py"), ExceptionsWriter.Script);
        File.WriteAllText(Path.Combine(dir, "exceptions.yaml"), register);
        if (zapRules is not null)
        {
            Directory.CreateDirectory(Path.Combine(dir, "security"));
            File.WriteAllText(Path.Combine(dir, "security/zap-rules.tsv"), zapRules);
        }
        var psi = new ProcessStartInfo("python3") { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = dir };
        foreach (var a in new[] { "-I", "exceptions.py", "exceptions.yaml", "--today", today, "--out", dir }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output, dir);
    }

    private const string Valid = """
        exceptions:
          - id: CVE-2026-1111
            scanner: trivy
            reason: Not reachable # trailing comment
            owner: team-orders
            ticket: SEC-1
            expires: 2026-12-31
          - id: CVE-2026-2222
            scanner: dependency-check
            reason: "False positive <script>alert(1)</script>"
            owner: team-orders
            ticket: SEC-2
            expires: 2027-01-31
          - id: GHSA-abcd-efgh-ijkl
            scanner: dependency-check
            reason: Test scope only
            owner: team-orders
            ticket: SEC-3
            expires: 2027-01-31
          - id: SNYK-JAVA-ORGFOO-123
            scanner: snyk
            reason: No fix available
            owner: team-orders
            ticket: SEC-4
            expires: 2027-02-28
          - id: 0123abc:src/app.properties:generic-api-key:12
            scanner: gitleaks
            reason: Test fixture key
            owner: team-orders
            ticket: SEC-5
            expires: 2027-03-31
          - id: '10038'
            scanner: zap
            reason: CSP set by the ingress
            owner: platform
            ticket: SEC-6
            expires: 2026-11-30
        """;

    [Fact]
    public void Valid_register_renders_every_ignore_file()
    {
        var (code, output, dir) = Run(Valid, zapRules: "10038\tFAIL\t(CSP)\n10020\tWARN\t(Anti-clickjacking)\n");
        Assert.True(code == 0, output);
        Assert.Contains("CVE-2026-1111 exp:2026-12-31", File.ReadAllText(Path.Combine(dir, "security/.trivyignore")));
        var xml = File.ReadAllText(Path.Combine(dir, "security/dependency-check-suppressions.xml"));
        Assert.Contains("<suppress until=\"2027-01-31Z\">", xml);
        Assert.Contains("<cve>CVE-2026-2222</cve>", xml);
        Assert.Contains("<vulnerabilityName>GHSA-abcd-efgh-ijkl</vulnerabilityName>", xml);
        Assert.Contains("&lt;script&gt;", xml);
        Assert.DoesNotContain("<script>", xml);
        System.Xml.Linq.XDocument.Parse(xml);
        Assert.Contains("\"SNYK-JAVA-ORGFOO-123\"", File.ReadAllText(Path.Combine(dir, ".snyk")));
        Assert.Contains("0123abc:src/app.properties:generic-api-key:12", File.ReadAllText(Path.Combine(dir, ".gitleaksignore")));
        var zap = File.ReadAllText(Path.Combine(dir, "security/zap-rules.tsv"));
        Assert.Contains("10038\tIGNORE\t(SEC-6, until 2026-11-30)", zap);
        Assert.DoesNotContain("10038\tFAIL", zap);
        Assert.Contains("10020\tWARN", zap);
        Assert.Contains("| zap | `10038` |", File.ReadAllText(Path.Combine(dir, "reports/exceptions.md")));
    }

    [Theory]
    [InlineData("2027-01-01", "expired on 2026-12-31")]
    [InlineData("2026-01-01", "more than 180 days")]
    public void Expiry_is_enforced(string today, string message)
    {
        var (code, output, _) = Run(Valid, today);
        Assert.Equal(1, code);
        Assert.Contains(message, output);
    }

    [Theory]
    [InlineData("    owner: team-orders\n", "", "missing owner")]
    [InlineData("    scanner: trivy\n", "    scanner: nessus\n", "unknown scanner")]
    [InlineData("  - id: CVE-2026-1111\n", "  - id: CVE-1; rm -rf /\n", "not a valid trivy id")]
    [InlineData("    ticket: SEC-1\n", "    ticket: SEC 1 $(id)\n", "ticket must be")]
    [InlineData("    reason: Not reachable # trailing comment\n", "    colour: blue\n", "expected one of")]
    public void Bad_entries_fail_the_build(string find, string replace, string message)
    {
        var register = Valid.Replace("\r\n", "\n");
        var first = register.IndexOf(find, StringComparison.Ordinal);
        Assert.True(first >= 0, find);
        register = register[..first] + replace + register[(first + find.Length)..];
        var (code, output, _) = Run(register);
        Assert.Equal(1, code);
        Assert.Contains(message, output);
    }

    [Fact]
    public void Generated_template_is_a_valid_empty_register()
    {
        var (code, output, dir) = Run(Files(Register())["security/exceptions.yaml"]);
        Assert.True(code == 0, output);
        Assert.True(File.Exists(Path.Combine(dir, ".snyk")));
    }
}
