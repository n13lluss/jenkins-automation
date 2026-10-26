using System.Text;

namespace PipelineForge.Core.Validation;

/// <summary>
/// Escaping for values written into generated Groovy and YAML. Values are always
/// validated by <see cref="ConfigValidator"/> first; escaping is the second layer.
/// </summary>
public static class GroovyEscaper
{
    /// <summary>A Groovy single-quoted string literal. Single quotes never interpolate.</summary>
    public static string Quote(string value)
    {
        var sb = new StringBuilder(value.Length + 2).Append('\'');
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case '\'': sb.Append(@"\'"); break;
                case '\n': sb.Append(@"\n"); break;
                case '\r': sb.Append(@"\r"); break;
                case '\t': sb.Append(@"\t"); break;
                default:
                    if (char.IsControl(c)) sb.Append($"\\u{(int)c:x4}");
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('\'').ToString();
    }

    /// <summary>A YAML double-quoted scalar.</summary>
    public static string Yaml(string value)
    {
        var sb = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append(@"\n"); break;
                case '\r': sb.Append(@"\r"); break;
                case '\t': sb.Append(@"\t"); break;
                default:
                    if (char.IsControl(c)) sb.Append($"\\u{(int)c:x4}");
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    /// <summary>A value for a Java .properties file.</summary>
    public static string Property(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case '\n': sb.Append(@"\n"); break;
                case '\r': sb.Append(@"\r"); break;
                case '=': sb.Append(@"\="); break;
                case ':': sb.Append(@"\:"); break;
                case '#': sb.Append(@"\#"); break;
                case '!': sb.Append(@"\!"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
