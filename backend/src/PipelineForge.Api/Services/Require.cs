using PipelineForge.Core.Validation;

namespace PipelineForge.Api.Services;

/// <summary>Input checks for request fields that are not part of a pipeline config.</summary>
public static class Require
{
    public static string Name(string? value, string field, int max = 100)
    {
        var v = value?.Trim() ?? "";
        if (v.Length == 0 || v.Length > max || v.Any(char.IsControl))
            throw new ArgumentException($"{field} must be 1-{max} printable characters.");
        return v;
    }

    public static string Url(string? value, string field)
    {
        if (!ConfigValidator.IsHttpUrl(value)) throw new ArgumentException($"{field} must be an http(s) URL without credentials.");
        return value!.TrimEnd('/');
    }

    public static string Repository(string? value)
    {
        var parts = (value ?? "").Split('/');
        if (parts.Length != 2 || parts.Any(p => p.Length is 0 or > 200 || p is "." or ".." || p.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))))
            throw new ArgumentException("Repository must look like owner/name.");
        return value!;
    }

    public static string Branch(string? value)
    {
        if (!ConfigValidator.IsBranchPattern(value) || value!.Contains('*')) throw new ArgumentException("Invalid branch name.");
        return value;
    }

    public static string RelativePath(string? value)
    {
        if (!ConfigValidator.IsRelativePath(value)) throw new ArgumentException("Invalid repository path.");
        return value!;
    }

    public static string Secret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 8192) throw new ArgumentException("A secret (token or API key) is required.");
        return value.Trim();
    }

    public static string? Optional(string? value, string field, int max = 200) =>
        string.IsNullOrWhiteSpace(value) ? null : Name(value, field, max);
}
