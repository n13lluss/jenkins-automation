using System.Text.Json;
using PipelineForge.Core;
using PipelineForge.Core.Generation;
using PipelineForge.Core.Model;
using PipelineForge.Core.Presets;

namespace PipelineForge.Cli;

/// <summary>
/// Offline generator: the same validation and output as the web app, from a config file. No network, no secrets.
///   pipelineforge generate --config pipeline.json --out ./out [--preset builtin-secure-java] [--force]
///   pipelineforge validate --config pipeline.json [--preset ...]
///   pipelineforge presets
/// </summary>
public static class Program
{
    private const int MaxConfigBytes = 1024 * 1024;

    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            stdout.WriteLine("usage: pipelineforge generate --config <file> --out <dir> [--preset <id>] [--force]");
            stdout.WriteLine("       pipelineforge validate --config <file> [--preset <id>]");
            stdout.WriteLine("       pipelineforge presets");
            return args.Length == 0 ? 2 : 0;
        }
        try
        {
            var options = ParseOptions(args.Skip(1).ToArray());
            return args[0] switch
            {
                "generate" => Generate(options, stdout, stderr),
                "validate" => Validate(options, stdout, stderr),
                "presets" => Presets(stdout),
                _ => Fail(stderr, $"Unknown command '{args[0]}'."),
            };
        }
        catch (Exception e) when (e is ArgumentException or IOException or JsonException or UnauthorizedAccessException)
        {
            return Fail(stderr, e.Message);
        }
    }

    private static Dictionary<string, string?> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string?>();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Unexpected argument '{a}'.");
            if (a == "--force") { options["force"] = "true"; continue; }
            if (i + 1 >= args.Length) throw new ArgumentException($"{a} needs a value.");
            options[a[2..]] = args[++i];
        }
        return options;
    }

    private static PipelineConfig LoadConfig(Dictionary<string, string?> options)
    {
        var path = options.GetValueOrDefault("config") ?? throw new ArgumentException("--config is required.");
        var info = new FileInfo(path);
        if (!info.Exists) throw new IOException($"{path} not found.");
        if (info.Length > MaxConfigBytes) throw new IOException($"{path} is larger than 1 MB.");
        var config = JsonSerializer.Deserialize<PipelineConfig>(File.ReadAllText(path), JsonDefaults.Options) ?? throw new ArgumentException("The config is empty.");
        if (options.GetValueOrDefault("preset") is { } id)
        {
            var preset = BuiltInPresets.Find(id) ?? throw new ArgumentException($"Unknown preset '{id}'. Run 'pipelineforge presets'.");
            config = PresetMerger.Apply(config, preset.ConfigJson);
        }
        return config;
    }

    private static GenerationResult GenerateChecked(Dictionary<string, string?> options, TextWriter stdout, TextWriter stderr)
    {
        var result = PipelineGenerator.Generate(LoadConfig(options));
        foreach (var c in result.Compliance.Changes) stdout.WriteLine($"compliance: {c}");
        foreach (var w in result.Compliance.Warnings) stderr.WriteLine($"warning: {w}");
        foreach (var e in result.Errors) stderr.WriteLine($"error: {e.Path}: {e.Message}");
        return result;
    }

    private static int Validate(Dictionary<string, string?> options, TextWriter stdout, TextWriter stderr)
    {
        var result = GenerateChecked(options, stdout, stderr);
        if (!result.Success) return 1;
        stdout.WriteLine($"valid: {result.Files.Count} files would be generated");
        return 0;
    }

    private static int Generate(Dictionary<string, string?> options, TextWriter stdout, TextWriter stderr)
    {
        var outDir = options.GetValueOrDefault("out") ?? throw new ArgumentException("--out is required.");
        var result = GenerateChecked(options, stdout, stderr);
        if (!result.Success) return 1;
        var root = Path.GetFullPath(outDir);
        var force = options.ContainsKey("force");
        foreach (var f in result.Files)
        {
            var target = Path.GetFullPath(Path.Combine(root, f.Path));
            // Generated paths are validated, but never write outside the output directory.
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new IOException($"Refusing to write {f.Path} outside {root}.");
            if (File.Exists(target) && !force)
                throw new IOException($"{f.Path} already exists; use --force to overwrite.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, f.Content);
        }
        stdout.WriteLine($"wrote {result.Files.Count} files to {root}");
        foreach (var c in result.Credentials) stdout.WriteLine($"credential: {c.Id} ({c.Kind}) - {c.Purpose}");
        return 0;
    }

    private static int Presets(TextWriter stdout)
    {
        foreach (var p in BuiltInPresets.All) stdout.WriteLine($"{p.Id}\t{p.Name}");
        return 0;
    }

    private static int Fail(TextWriter stderr, string message)
    {
        stderr.WriteLine($"error: {message}");
        return 2;
    }
}
