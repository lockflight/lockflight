namespace Lockflight.Cli.Hooks;

/// <summary>
/// One simple command after its prefixes are peeled off: variable assignments, keywords (<c>if</c>, <c>then</c>, <c>do</c>, <c>!</c>,
/// <c>{</c>, …) and wrappers that run the next word as a program (<c>command</c>, <c>env</c>, <c>sudo</c>, <c>time</c>, <c>nice</c>,
/// <c>nohup</c>, <c>exec</c>, <c>timeout</c>, <c>xargs</c>). <see cref="Program"/> is the lower-cased file name without
/// <c>.exe</c>; <see cref="ArgumentsFromInput"/> is set behind <c>xargs</c>, whose arguments come from stdin and are not seen.
/// </summary>
internal sealed record ShellCommand(string Program, IReadOnlyList<string> Arguments, bool ArgumentsFromInput)
{
    /// <summary>Whether the command reads a pipeline (<c>… | Remove-Item</c>): in PowerShell its targets may come from there.</summary>
    public bool PipedInput { get; init; }

    private static readonly HashSet<string> Keywords =
        ["!", "{", "}", "if", "then", "else", "elif", "fi", "do", "done", "while", "until", "esac", "coproc", "nohup", "noglob"];

    private static readonly Dictionary<string, HashSet<string>> WrapperOptionsWithValue = new()
    {
        ["env"] = ["-u", "--unset", "-C", "--chdir", "-S", "--split-string"],
        ["sudo"] = ["-u", "-g", "-h", "-p", "-C", "-D", "-r", "-t", "-U", "-T", "--user", "--group", "--host", "--prompt", "--chdir"],
        ["doas"] = ["-u", "-C"],
        ["exec"] = ["-a"],
        ["command"] = [],
        ["builtin"] = [],
        ["time"] = [],
        ["busybox"] = [],
        ["nice"] = ["-n", "--adjustment"],
        ["timeout"] = ["-s", "--signal", "-k", "--kill-after"],
        ["stdbuf"] = ["-i", "-o", "-e"],
        ["xargs"] = ["-I", "-n", "-P", "-L", "-d", "-E", "-s", "-a", "--arg-file", "--delimiter", "--max-args", "--max-procs", "--replace"],
    };

    public static ShellCommand? Parse(IReadOnlyList<string> words)
    {
        int index = 0;
        bool argumentsFromInput = false;
        while (index < words.Count)
        {
            string word = words[index];
            string name = ProgramName(word);
            if (ShellAssignment.IsAssignment(word) || Keywords.Contains(name))
            {
                index++;
            }
            else if (WrapperOptionsWithValue.TryGetValue(name, out HashSet<string>? optionsWithValue))
            {
                argumentsFromInput |= name == "xargs";
                index = SkipWrapper(words, index + 1, optionsWithValue, takesDuration: name == "timeout");
            }
            else
            {
                break;
            }
        }

        return index < words.Count ? new ShellCommand(ProgramName(words[index]), [.. words.Skip(index + 1)], argumentsFromInput) : null;
    }

    public static string ProgramName(string word)
    {
        string name = Path.GetFileName(word.Replace('\\', '/')).ToLowerInvariant();
        return name.EndsWith(".exe", StringComparison.Ordinal) ? name[..^4] : name;
    }

    private static int SkipWrapper(IReadOnlyList<string> words, int index, HashSet<string> optionsWithValue, bool takesDuration)
    {
        while (index < words.Count && (words[index].StartsWith('-') || ShellAssignment.IsAssignment(words[index])))
        {
            index += optionsWithValue.Contains(words[index]) ? 2 : 1;
        }

        return takesDuration && index < words.Count ? index + 1 : index;
    }
}
