namespace Lockflight.Cli.Hooks;

/// <summary>
/// Commands that write or delete the files they name: <c>rm</c> (its operands are also trees: <c>rm -r</c>), <c>unlink</c>,
/// <c>shred</c>, <c>truncate</c>, <c>touch</c>, <c>chmod</c>/<c>chown</c>, <c>tee</c>, <c>dd of=</c>, <c>curl -o</c>,
/// <c>wget -O</c>, and <c>find</c> with <c>-delete</c> or an <c>-exec</c> of a writer or of a shell whose command string writes (its
/// start directories are trees).
/// </summary>
internal static class FileCommandTargets
{
    private const string DdOutput = "of=";

    private static readonly HashSet<string> OperandWriters = ["rm", "unlink", "shred", "truncate", "touch", "chmod", "chown", "chgrp", "tee"];
    private static readonly string[] OptionsWithValue = ["-s", "--size", "-r", "--reference", "-t", "-d", "--date", "-n", "--iterations"];
    private static readonly HashSet<string> FindExecutors = ["-exec", "-execdir", "-ok", "-okdir"];
    private static readonly HashSet<string> FindPathFilters = ["-path", "-ipath", "-wholename", "-iwholename", "-regex", "-iregex", "-o", "-or", "!", "-not"];

    public static IReadOnlySet<string> Programs { get; } = new HashSet<string>([.. OperandWriters, "dd", "find", "curl", "wget"]);

    /// <summary>The writes of a command; <paramref name="writes"/> says whether the command <c>find -exec</c> runs (its words) writes.</summary>
    public static CommandWrites Of(string program, IReadOnlyList<string> arguments, Func<IReadOnlyList<string>, bool> writes) => program switch
    {
        "rm" => CommandWrites.OfFilesAndTrees(ShellArguments.Operands(arguments, [])),
        "dd" => CommandWrites.OfFiles([.. arguments.Where(argument => argument.StartsWith(DdOutput, StringComparison.Ordinal)).Select(argument => argument[DdOutput.Length..])]),
        "curl" => CommandWrites.OfFiles(Present(ShellArguments.OptionValue(arguments, "-o", "--output"))),
        "wget" => CommandWrites.OfFiles(Present(ShellArguments.OptionValue(arguments, "-O", "--output-document"))),
        "find" => Find(arguments, writes),
        _ when OperandWriters.Contains(program) => CommandWrites.OfFiles(ShellArguments.Operands(arguments, OptionsWithValue)),
        _ => CommandWrites.None,
    };

    private static List<string> Present(string? path) => path is null or "-" ? [] : [path];

    /// <summary>
    /// <c>find DIR… -delete</c> or <c>-exec rm {} ;</c> may touch anything under its start directories (default <c>.</c>). A plain
    /// <c>-delete</c> limited by <c>-name</c>/<c>-iname</c> touches only names matching those patterns below the start directories.
    /// </summary>
    private static CommandWrites Find(IReadOnlyList<string> arguments, Func<IReadOnlyList<string>, bool> writes)
    {
        List<string> starts = [.. arguments.SkipWhile(argument => argument is "-H" or "-L" or "-P").TakeWhile(argument => !argument.StartsWith('-') && argument is not ("(" or "!"))];
        starts = starts.Count == 0 ? [ShellArguments.CurrentDirectory] : starts;
        bool deletes = arguments.Contains("-delete");
        bool otherWrites = arguments.Any(argument => argument is "-fprint" or "-fprintf" or "-fls")
            || ExecutedCommands(arguments).Any(writes);
        if (!deletes && !otherWrites)
        {
            return CommandWrites.None;
        }

        List<string> names = [.. arguments.Select((argument, index) => argument is "-name" or "-iname" && index + 1 < arguments.Count ? arguments[index + 1] : null).OfType<string>()];
        bool otherFilters = arguments.Any(argument => FindPathFilters.Contains(argument));
        if (otherWrites || otherFilters || names.Count == 0)
        {
            return CommandWrites.OfTrees(starts);
        }

        return CommandWrites.None with { PatternFiles = [.. starts.SelectMany(start => names.Select(name => ShellArguments.Join(start, "**/" + name)))] };
    }

    /// <summary>The words of each command an <c>-exec</c>, <c>-execdir</c>, <c>-ok</c> or <c>-okdir</c> runs, up to its <c>;</c> or <c>+</c>.</summary>
    private static IEnumerable<IReadOnlyList<string>> ExecutedCommands(IReadOnlyList<string> arguments) =>
        arguments.Select((argument, index) => (argument, index))
            .Where(entry => FindExecutors.Contains(entry.argument))
            .Select(entry => (IReadOnlyList<string>)[.. arguments.Skip(entry.index + 1).TakeWhile(word => word is not (";" or "+"))]);
}
