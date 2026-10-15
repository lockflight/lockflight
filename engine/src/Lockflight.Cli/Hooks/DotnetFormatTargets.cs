namespace Lockflight.Cli.Hooks;

/// <summary>
/// <c>dotnet format</c> rewrites source files only: its <c>--include</c> paths, else everything under its project, solution or
/// folder argument, else everything under the current directory. <c>--verify-no-changes</c> writes nothing.
/// </summary>
internal static class DotnetFormatTargets
{
    private static readonly HashSet<string> OptionsWithValue =
        ["--exclude", "--severity", "--diagnostics", "--exclude-diagnostics", "--verbosity", "-v", "--report", "--binarylog", "--include"];

    /// <summary>The source trees of a <c>dotnet</c> invocation; empty unless it is <c>dotnet format</c>.</summary>
    public static CommandWrites Of(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0 || arguments[0] != "format" || arguments.Contains("--verify-no-changes"))
        {
            return CommandWrites.None;
        }

        List<string> includes = [];
        List<string> positional = [];
        for (int index = 1; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            if (argument == "--include")
            {
                while (index + 1 < arguments.Count && !arguments[index + 1].StartsWith('-'))
                {
                    includes.Add(arguments[++index]);
                }
            }
            else if (OptionsWithValue.Contains(argument))
            {
                index++;
            }
            else if (!argument.StartsWith('-') && argument is not ("whitespace" or "style" or "analyzers"))
            {
                positional.Add(argument);
            }
        }

        List<string> trees = includes.Count > 0 ? includes
            : positional.Count == 0 ? [ShellArguments.CurrentDirectory]
            : [.. positional.Select(DirectoryOfWorkspace)];
        return new CommandWrites([], [], trees, null);
    }

    /// <summary>A project or solution file stands for its directory; a folder for itself.</summary>
    private static string DirectoryOfWorkspace(string path)
    {
        if (!Path.HasExtension(path) || path.EndsWith('.'))
        {
            return path;
        }

        // Not Path.GetDirectoryName: it would rewrite the separators to the OS's, and the path is matched as written.
        int lastSeparator = path.LastIndexOfAny(['/', '\\']);
        return lastSeparator <= 0 ? ShellArguments.CurrentDirectory : path[..lastSeparator];
    }
}
