namespace Lockflight.Cli.Hooks;

/// <summary>
/// <c>cp</c>, <c>mv</c>, <c>install</c>, <c>ln</c> and <c>rsync</c>. The destination may be a file or a directory, so it counts as
/// written and so does <c>DEST/&lt;name of each source&gt;</c> (also with <c>-t DIR</c>/<c>--target-directory=DIR</c>). A copy that
/// can create a directory tree (<c>cp -r</c>, every <c>mv</c>, <c>rsync</c>) or whose source names are unknown (<c>$VAR</c>) makes the
/// destination a tree. <c>mv</c> also removes its sources.
/// </summary>
internal static class CopyCommandTargets
{
    private static readonly string[] OptionsWithValue =
        ["-t", "--target-directory", "-S", "--suffix", "-m", "--mode", "-o", "--owner", "-g", "--group"];

    private static readonly string[] RsyncOptionsWithValue =
        ["-e", "--rsh", "--exclude", "--include", "--filter", "-f", "--exclude-from", "--include-from", "--files-from", "--chmod", "--backup-dir", "--temp-dir", "-T", "--partial-dir", "--log-file", "--port", "--rsync-path"];

    public static IReadOnlySet<string> Programs { get; } = new HashSet<string>(["cp", "mv", "install", "ln", "rsync"]);

    public static CommandWrites Of(string program, IReadOnlyList<string> arguments)
    {
        if (program == "rsync")
        {
            return Rsync(arguments);
        }

        List<string> operands = ShellArguments.Operands(arguments, OptionsWithValue);
        if (program == "install" && ShellArguments.HasFlag(arguments, "d", "--directory"))
        {
            return CommandWrites.OfFiles(operands);
        }

        string? targetDirectory = ShellArguments.OptionValue(arguments, "-t", "--target-directory");
        if (operands.Count == 0)
        {
            return CommandWrites.None;
        }

        (string destination, List<string> sources) = targetDirectory is not null
            ? (targetDirectory, operands)
            : operands.Count == 1 ? (ShellArguments.CurrentDirectory, operands) : (operands[^1], operands[..^1]);
        bool move = program == "mv";
        bool recursive = move || (program == "cp" && ShellArguments.HasFlag(arguments, "rRa", "--recursive", "--archive"));
        bool parents = program == "cp" && arguments.Contains("--parents");
        bool intoDirectory = !ShellArguments.HasFlag(arguments, "T", "--no-target-directory");

        List<string> files = targetDirectory is null && operands.Count > 1 ? [destination] : [];
        List<string> trees = [];
        bool unknownNames = false;
        foreach (string source in intoDirectory ? sources : [])
        {
            string? name = parents ? source.TrimStart('/') : ShellArguments.BaseName(source);
            if (name is null || name.IndexOfAny(['$', '`']) >= 0)
            {
                unknownNames = true;
                continue;
            }

            files.Add(ShellArguments.Join(destination, name));
        }

        if (recursive || unknownNames)
        {
            trees.Add(destination);
        }

        if (move)
        {
            files.AddRange(sources);
            trees.AddRange(sources);
        }

        return new CommandWrites(files, trees, [], null);
    }

    private static CommandWrites Rsync(IReadOnlyList<string> arguments)
    {
        List<string> operands = ShellArguments.Operands(arguments, RsyncOptionsWithValue);
        if (operands.Count < 2)
        {
            return CommandWrites.None;
        }

        string destination = operands[^1];
        List<string> sources = operands[..^1];
        List<string> files = [destination, .. sources.Select(ShellArguments.BaseName).OfType<string>().Select(name => ShellArguments.Join(destination, name))];
        if (arguments.Contains("--remove-source-files"))
        {
            files.AddRange(sources);
        }

        return new CommandWrites(files, [destination], [], null);
    }
}
