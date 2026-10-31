namespace Lockflight.Cli.Hooks;

/// <summary>
/// <c>cp</c>, <c>mv</c>, <c>install</c> and <c>ln</c> (a link replaces its destination as a copy does). The destination may be a file or a directory, so it counts as written and so does
/// <c>DEST/&lt;name of each source&gt;</c> (also with <c>-t DIR</c>/<c>--target-directory=DIR</c>). A copy that can create a directory
/// tree (<c>cp -r</c>, every <c>mv</c>) makes the destination a tree; <c>mv</c> also removes its sources.
/// </summary>
internal static class CopyCommandTargets
{
    private static readonly string[] OptionsWithValue =
        ["-t", "--target-directory", "-S", "--suffix", "-m", "--mode", "-o", "--owner", "-g", "--group"];

    public static IReadOnlySet<string> Programs { get; } = new HashSet<string>(["cp", "mv", "install", "ln"]);

    public static CommandWrites Of(string program, IReadOnlyList<string> arguments)
    {
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
        bool intoDirectory = !ShellArguments.HasFlag(arguments, "T", "--no-target-directory");

        List<string> files = targetDirectory is null && operands.Count > 1 ? [destination] : [];
        files.AddRange((intoDirectory ? sources : []).Select(ShellArguments.BaseName).OfType<string>().Select(name => ShellArguments.Join(destination, name)));
        List<string> trees = recursive ? [destination] : [];
        if (move)
        {
            files.AddRange(sources);
            trees.AddRange(sources);
        }

        return new CommandWrites(files, trees, []);
    }
}
