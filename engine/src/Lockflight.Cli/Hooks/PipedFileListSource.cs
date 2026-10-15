namespace Lockflight.Cli.Hooks;

/// <summary>
/// The directories a file list piped into <c>xargs</c> is confined to, when the command before the pipe is one that only lists
/// files under the paths it names: <c>grep -l</c>/<c>-L</c> (any recursion) and <c>find</c>. A writer behind <c>xargs</c> then
/// writes at most anything under those paths (a tree), which the guard can check, instead of hidden targets. Anything else, or a
/// listing whose paths contain a substitution, stays unknown (null).
/// </summary>
internal static class PipedFileListSource
{
    private static readonly HashSet<string> GrepListFlags = ["--files-with-matches", "--files-without-match"];

    private static readonly HashSet<string> GrepOptionsWithValue =
        ["-e", "-f", "-m", "-A", "-B", "-C", "--regexp", "--file", "--max-count", "--include", "--exclude", "--exclude-dir", "--context"];

    public static IReadOnlyList<string>? Directories(ShellCommand upstream) => upstream.ArgumentsFromInput ? null : upstream.Program switch
    {
        "grep" or "egrep" or "fgrep" or "rg" => Grep(upstream.Arguments),
        "find" => Find(upstream.Arguments),
        _ => null,
    };

    private static List<string>? Grep(IReadOnlyList<string> arguments)
    {
        bool lists = false;
        bool patternGiven = false;
        List<string> operands = [];
        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            if (argument == "--")
            {
                operands.AddRange(arguments.Skip(index + 1));
                break;
            }

            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                string name = argument.Split('=', 2)[0];
                lists |= GrepListFlags.Contains(name);
                patternGiven |= name is "--regexp" or "--file";
                index += GrepOptionsWithValue.Contains(name) && !argument.Contains('=', StringComparison.Ordinal) ? 1 : 0;
            }
            else if (argument.StartsWith('-') && argument.Length > 1)
            {
                lists |= argument.Contains('l', StringComparison.Ordinal) || argument.Contains('L', StringComparison.Ordinal);
                patternGiven |= argument is "-e" or "-f";
                index += GrepOptionsWithValue.Contains(argument) ? 1 : 0;
            }
            else
            {
                operands.Add(argument);
            }
        }

        if (!lists)
        {
            return null;
        }

        List<string> paths = patternGiven ? operands : [.. operands.Skip(1)];
        return Confined(paths.Count == 0 ? ["."] : paths);
    }

    private static List<string>? Find(IReadOnlyList<string> arguments)
    {
        List<string> paths = [.. arguments.TakeWhile(argument => !argument.StartsWith('-') && argument is not "(" and not "!")];
        return Confined(paths.Count == 0 ? ["."] : paths);
    }

    private static List<string>? Confined(List<string> paths) =>
        paths.Any(path => path.Contains(ShellTokenizer.SubstitutionPlaceholder, StringComparison.Ordinal)) ? null : paths;
}
