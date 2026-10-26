namespace PipelineForge.Core.Importing;

/// <summary>
/// Minimal lexical helper for Groovy: knows which characters are code (not inside
/// strings or comments) and matches braces. Enough to split a declarative pipeline.
/// </summary>
public sealed class GroovyText
{
    public string Text { get; }
    private readonly bool[] _code;

    public GroovyText(string text)
    {
        Text = text.Replace("\r\n", "\n");
        _code = BuildMask(Text);
    }

    public bool IsCode(int index) => index >= 0 && index < _code.Length && _code[index];

    private static bool[] BuildMask(string t)
    {
        var mask = new bool[t.Length];
        var i = 0;
        while (i < t.Length)
        {
            if (t[i] == '/' && i + 1 < t.Length && t[i + 1] == '/')
            {
                while (i < t.Length && t[i] != '\n') i++;
                continue;
            }
            if (t[i] == '/' && i + 1 < t.Length && t[i + 1] == '*')
            {
                var end = t.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? t.Length : end + 2;
                continue;
            }
            if (t[i] is '\'' or '"')
            {
                var q = t[i];
                var triple = i + 2 < t.Length && t[i + 1] == q && t[i + 2] == q;
                var delim = triple ? 3 : 1;
                i += delim;
                while (i < t.Length)
                {
                    if (t[i] == '\\') { i += 2; continue; }
                    if (t[i] == q && (!triple || (i + 2 < t.Length && t[i + 1] == q && t[i + 2] == q)))
                    {
                        i += delim;
                        break;
                    }
                    if (!triple && t[i] == '\n') { i++; break; }
                    i++;
                }
                continue;
            }
            mask[i] = true;
            i++;
        }
        return mask;
    }

    /// <summary>Index of the matching close brace for the open brace at <paramref name="open"/>, or -1.</summary>
    public int MatchBrace(int open)
    {
        var depth = 0;
        for (var i = open; i < Text.Length; i++)
        {
            if (!_code[i]) continue;
            if (Text[i] == '{') depth++;
            else if (Text[i] == '}' && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>First code occurrence of <paramref name="token"/> in [from, to).</summary>
    public int IndexOfCode(string token, int from, int to)
    {
        var i = from;
        while (true)
        {
            i = Text.IndexOf(token, i, StringComparison.Ordinal);
            if (i < 0 || i >= to) return -1;
            if (IsCode(i) && (i == 0 || !char.IsLetterOrDigit(Text[i - 1]))) return i;
            i++;
        }
    }

    /// <summary>Finds "keyword ... {" at code level in a range and returns the content bounds.</summary>
    public (int Start, int End)? Block(string keyword, int from, int to)
    {
        var i = from;
        while (true)
        {
            var k = IndexOfCode(keyword, i, to);
            if (k < 0) return null;
            var after = k + keyword.Length;
            var j = after;
            while (j < to && char.IsWhiteSpace(Text[j])) j++;
            if (j < to && Text[j] == '{')
            {
                var close = MatchBrace(j);
                if (close < 0 || close > to) return null;
                return (j + 1, close);
            }
            i = after;
        }
    }

    public string Slice((int Start, int End) range) => Text[range.Start..range.End];
}
