namespace Lockflight.Cli.Hooks;

/// <summary>
/// Commands whose written files are named inside their input, not on the command line: <c>tar x</c>, <c>unzip</c> and <c>patch</c>.
/// Each may rewrite anything under its output directory (<c>-C</c>, <c>-d</c>; else the current directory), which becomes a tree.
/// </summary>
internal static class ExtractCommandTargets
{
    private static readonly HashSet<string> UnzipReadOnlyFlags = ["-l", "-t", "-v", "-p", "-z", "-Z", "-c", "-T"];
    private static readonly HashSet<string> PatchReadOnlyFlags = ["--dry-run", "--check", "-C"];

    public static IReadOnlySet<string> Programs { get; } = new HashSet<string>(["tar", "bsdtar", "unzip", "patch"]);

    public static CommandWrites Of(string program, IReadOnlyList<string> arguments) => program switch
    {
        "tar" or "bsdtar" => Tar(arguments),
        "unzip" => arguments.Any(UnzipReadOnlyFlags.Contains)
            ? CommandWrites.None
            : CommandWrites.OfTrees([ShellArguments.OptionValue(arguments, "-d", null) ?? ShellArguments.CurrentDirectory]),
        "patch" => Patch(arguments),
        _ => CommandWrites.None,
    };

    /// <summary>Extraction is <c>x</c> in the first (old-style) word or a short cluster, or <c>--extract</c>/<c>--get</c>.</summary>
    private static CommandWrites Tar(IReadOnlyList<string> arguments)
    {
        bool oldStyleExtract = arguments.Count > 0 && !arguments[0].StartsWith('-') && arguments[0].Contains('x', StringComparison.Ordinal);
        bool extract = oldStyleExtract || ShellArguments.HasFlag(arguments, "x", "--extract", "--get");
        if (!extract)
        {
            return CommandWrites.None;
        }

        string directory = ShellArguments.OptionValue(arguments, "-C", "--directory") ?? ShellArguments.CurrentDirectory;
        return CommandWrites.OfTrees([directory]);
    }

    /// <summary><c>patch -o FILE</c> writes that file only; otherwise the files named in the patch, under <c>-d DIR</c> or here.</summary>
    private static CommandWrites Patch(IReadOnlyList<string> arguments)
    {
        if (arguments.Any(PatchReadOnlyFlags.Contains))
        {
            return CommandWrites.None;
        }

        if (ShellArguments.OptionValue(arguments, "-o", "--output") is string output)
        {
            return CommandWrites.OfFiles([output]);
        }

        string directory = ShellArguments.OptionValue(arguments, "-d", "--directory") ?? ShellArguments.CurrentDirectory;
        return CommandWrites.OfTrees([directory]);
    }
}
