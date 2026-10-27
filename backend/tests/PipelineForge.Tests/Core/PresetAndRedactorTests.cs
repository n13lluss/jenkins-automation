using PipelineForge.Core.Model;
using PipelineForge.Core.Presets;
using PipelineForge.Core.Security;

namespace PipelineForge.Tests.Core;

public class PresetTests
{
    private const string Preset = """{ "compliance": { "profile": "iso27001" }, "security": { "dast": { "mode": "full" } }, "aiReview": null }""";

    [Fact]
    public void Preset_is_merged_on_top_of_the_config()
    {
        var merged = PresetMerger.Apply(Samples.MavenHelm(), Preset);
        Assert.Equal(ComplianceProfile.Iso27001, merged.Compliance.Profile);
        Assert.Equal(DastMode.Full, merged.Security.Dast.Mode);
        Assert.Equal(["test"], merged.Security.Dast.Environments);
        Assert.False(merged.AiReview.Enabled);
    }

    [Fact]
    public void Locked_paths_win_over_user_edits()
    {
        var config = PresetMerger.Apply(Samples.MavenHelm(), Preset);
        config.Compliance.Profile = ComplianceProfile.None;
        Assert.Equal(["compliance.profile"], PresetMerger.Violations(config, Preset, ["compliance.profile"]));
        var enforced = PresetMerger.EnforceLocks(config, Preset, ["compliance.profile"]);
        Assert.Equal(ComplianceProfile.Iso27001, enforced.Compliance.Profile);
    }

    [Fact]
    public void Non_object_presets_are_rejected() =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => PresetMerger.Apply(Samples.MavenHelm(), "[1]"));
}

public class RedactorTests
{
    [Theory]
    [InlineData("password = \"hunter22hunter\"")]
    // Fake key assembled at runtime so the source itself does not trip secret scanners.
    [InlineData("AWS key " + "AKIA" + "ABCDEFGHIJKLMNOP here")]
    [InlineData("token: ghp_abcdefghijklmnopqrstuvwxyz0123456789AB")]
    [InlineData("postgres://admin:s3cr3t@db:5432/app")]
    [InlineData("key = sk-ant-api03-abcdefghijklmnopqrstuvwxyz")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\nMIIEow\n-----END RSA PRIVATE KEY-----")]
    [InlineData("blob Zx9Kq2Lm8Np4Rt6Vw1Ys3Ub5Hd7Fg0Jk2Qa9Wc4Er")]
    public void Secrets_are_redacted(string text)
    {
        var redacted = SecretRedactor.Redact(text);
        Assert.Contains(SecretRedactor.Marker, redacted);
    }

    [Theory]
    [InlineData("index 3f2a9c1d4b5e6f708192a3b4c5d6e7f809a1b2c3..4d5e6f708192a3b4c5d6e7f809a1b2c3d4e5f607")]
    [InlineData("public void HandleCustomerOrderSubmission()")]
    public void Ordinary_code_is_kept(string text) => Assert.Equal(text, SecretRedactor.Redact(text));
}
