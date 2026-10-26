using System.Text;

namespace PipelineForge.Core.Generation;

/// <summary>Tiny indentation-aware writer for Groovy and YAML.</summary>
public sealed class CodeWriter(string indentUnit = "    ")
{
    private readonly StringBuilder _sb = new();
    private int _depth;

    public CodeWriter Line(string text = "")
    {
        if (text.Length == 0) { _sb.Append('\n'); return this; }
        for (var i = 0; i < _depth; i++) _sb.Append(indentUnit);
        _sb.Append(text).Append('\n');
        return this;
    }

    /// <summary>Writes a multi-line block, indenting each line at the current depth.</summary>
    public CodeWriter Lines(string text)
    {
        foreach (var line in text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n')) Line(line.TrimEnd());
        return this;
    }

    public CodeWriter Block(string header, Action body, string open = " {", string close = "}")
    {
        Line(header + open);
        _depth++;
        body();
        _depth--;
        if (close.Length > 0) Line(close);
        return this;
    }

    public CodeWriter Indent(Action body)
    {
        _depth++;
        body();
        _depth--;
        return this;
    }

    public override string ToString() => _sb.ToString();
}
