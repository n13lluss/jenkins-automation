using System.Text.RegularExpressions;

namespace PipelineForge.Core.Security;

/// <summary>
/// Removes likely secrets before text leaves the system (AI calls, logs). Same rules as the
/// generated jenkins/scripts/ai_review.py: known token formats, key=value assignments and
/// high-entropy strings.
/// </summary>
public static partial class SecretRedactor
{
    public const string Marker = "[REDACTED]";

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----.*?-----END [A-Z ]*PRIVATE KEY-----", RegexOptions.Singleline)] private static partial Regex PrivateKeyRx();
    [GeneratedRegex(@"AKIA[0-9A-Z]{16}|gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{50,}|xox[abprs]-[A-Za-z0-9-]{10,}|sk-ant-[A-Za-z0-9_-]{20,}|sk-[A-Za-z0-9_-]{32,}|AIza[0-9A-Za-z_-]{35}|eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}|ATBB[A-Za-z0-9]{20,}")] private static partial Regex TokenRx();
    [GeneratedRegex(@"(?i)\b(?:mongodb(?:\+srv)?|postgres(?:ql)?|mysql|redis|amqps?)://[^\s:/@]+:[^\s@/]+@")] private static partial Regex ConnectionStringRx();
    [GeneratedRegex(@"(?i)((?:secret|password|passwd|pwd|token|api[_-]?key|client[_-]?secret|access[_-]?key)[A-Za-z0-9_.-]*\s*[:=]\s*['""]?)([^\s'""]{6,})")] private static partial Regex AssignmentRx();
    [GeneratedRegex(@"[A-Za-z0-9+/=_-]{32,}")] private static partial Regex CandidateRx();

    public static string Redact(string text)
    {
        text = PrivateKeyRx().Replace(text, Marker);
        text = TokenRx().Replace(text, Marker);
        text = ConnectionStringRx().Replace(text, Marker);
        text = AssignmentRx().Replace(text, m => m.Groups[1].Value + Marker);
        return CandidateRx().Replace(text, m =>
        {
            var token = m.Value;
            var mixed = token.Any(char.IsDigit) && token.Any(char.IsLetter);
            return mixed && Entropy(token) > 4.2 ? Marker : token;
        });
    }

    public static double Entropy(string value)
    {
        if (value.Length == 0) return 0;
        return value.GroupBy(c => c)
            .Select(g => (double)g.Count() / value.Length)
            .Sum(p => -p * Math.Log2(p));
    }
}
