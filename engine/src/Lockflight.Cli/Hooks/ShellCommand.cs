namespace Lockflight.Cli.Hooks;

/// <summary>
/// One simple command after its prefixes are peeled off: variable assignments, keywords (<c>if</c>, <c>then</c>, <c>do</c>, <c>!</c>,
/// <c>{</c>, <c>function NAME</c>, …) and wrappers that run the next word as a program (<c>command</c>, <c>env</c> (also the
/// command line of <c>env -S</c>), <c>sudo</c>, <c>time</c>, <c>nice</c>, <c>nohup</c>, <c>exec</c>, <c>timeout</c>,
/// <c>setsid</c>, <c>flock</c>, <c>caffeinate</c>, <c>arch</c>, <c>ionice</c>, <c>chronic</c>, <c>xargs</c>).
/// <see cref="Program"/> is the lower-cased file name without <c>.exe</c>; <see cref="ArgumentsFromInput"/> is set behind
/// <c>xargs</c>, whose arguments come from stdin and are not seen.
/// </summary>
internal sealed record ShellCommand(string Program, IReadOnlyList<string> Arguments, bool ArgumentsFromInput)
{
    /// <summary>Whether the command reads a pipeline (<c>… | Remove-Item</c>): in PowerShell its targets may come from there.</summary>
    public bool PipedInput { get; init; }

    /// <summary>The index of the program among the words parsed; -1 when it came from inside an <c>env -S</c> string.</summary>
    public int ProgramIndex { get; init; }

    private const string FunctionKeyword = "function";

    private static readonly HashSet<string> Keywords =
        ["!", "{", "}", "if", "then", "else", "elif", "fi", "do", "done", "while", "until", "esac", "coproc", "nohup", "noglob"];

    private static readonly HashSet<string> SplitStringOptions = ["-S", "--split-string"];

    private static readonly Dictionary<string, HashSet<string>> WrapperOptionsWithValue = new()
    {
        ["env"] = ["-u", "--unset", "-C", "--chdir"],
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
        ["setsid"] = [],
        ["flock"] = ["-w", "--wait", "--timeout", "-E", "--conflict-exit-code"],
        ["caffeinate"] = ["-t", "-w"],
        ["arch"] = ["-arch", "-e"],
        ["ionice"] = ["-c", "--class", "-n", "--classdata", "-p", "--pid", "-P", "--pgid", "-u", "--uid"],
        ["chronic"] = [],
        ["xargs"] = ["-I", "-n", "-P", "-L", "-d", "-E", "-s", "-a", "--arg-file", "--delimiter", "--max-args", "--max-procs", "--replace"],
    };

    /// <summary>Wrappers that take operands of their own before the program: <c>timeout DURATION</c>, <c>flock FILE</c>.</summary>
    private static readonly Dictionary<string, int> WrapperOperands = new() { ["timeout"] = 1, ["flock"] = 1 };

    public static ShellCommand? Parse(IReadOnlyList<string> words)
    {
        List<string> parsed = [.. words];
        int index = 0;
        bool argumentsFromInput = false;
        bool spliced = false;
        while (index < parsed.Count)
        {
            string word = parsed[index];
            string name = ProgramName(word);
            if (ShellAssignment.IsAssignment(word) || Keywords.Contains(name))
            {
                index++;
            }
            else if (name == FunctionKeyword)
            {
                // "function NAME { …; }": the body's first command follows the name (and "()", which the tokenizer takes apart).
                index += 2;
            }
            else if (name == "flock" && parsed.Skip(index + 1).Any(argument => argument is "-c" or "--command"))
            {
                // "flock FILE -c STRING" runs a command string (see NestedShellCommand), not the next word.
                break;
            }
            else if (WrapperOptionsWithValue.TryGetValue(name, out HashSet<string>? optionsWithValue))
            {
                argumentsFromInput |= name == "xargs";
                int start = index + 1;
                index = SkipWrapper(parsed, start, optionsWithValue, WrapperOperands.GetValueOrDefault(name));
                spliced |= name == "env" && SpliceSplitString(parsed, start, ref index);
            }
            else
            {
                break;
            }
        }

        return index < parsed.Count
            ? new ShellCommand(ProgramName(parsed[index]), [.. parsed.Skip(index + 1)], argumentsFromInput) { ProgramIndex = spliced ? -1 : index }
            : null;
    }

    public static string ProgramName(string word)
    {
        string name = Path.GetFileName(word.Replace('\\', '/')).ToLowerInvariant();
        return name.EndsWith(".exe", StringComparison.Ordinal) ? name[..^4] : name;
    }

    private static int SkipWrapper(List<string> words, int index, HashSet<string> optionsWithValue, int operands)
    {
        while (index < words.Count && (words[index].StartsWith('-') || ShellAssignment.IsAssignment(words[index])))
        {
            index += optionsWithValue.Contains(words[index]) || SplitStringOptions.Contains(words[index]) ? 2 : 1;
        }

        return Math.Min(index + operands, words.Count);
    }

    /// <summary>
    /// <c>env -S 'cmd args'</c> (also <c>-S'…'</c>, <c>--split-string=…</c>) splits the string into the command line it runs: its
    /// words replace the option, and the program is looked for among them. Returns whether a string was spliced in.
    /// </summary>
    private static bool SpliceSplitString(List<string> words, int start, ref int index)
    {
        for (int position = start; position < index && position < words.Count; position++)
        {
            string option = words[position];
            string? value = SplitStringOptions.Contains(option) && position + 1 < words.Count ? words[position + 1]
                : option.StartsWith("--split-string=", StringComparison.Ordinal) ? option["--split-string=".Length..]
                : option.StartsWith("-S", StringComparison.Ordinal) && option.Length > 2 ? option[2..]
                : null;
            if (value is null)
            {
                continue;
            }

            int removed = SplitStringOptions.Contains(option) ? 2 : 1;
            List<string> split = [.. ShellTokenizer.Tokenize(value, ShellDialect.Posix).Where(token => token.IsWord).Select(token => token.Text)];
            words.RemoveRange(position, removed);
            words.InsertRange(position, split);
            index = position;
            return true;
        }

        return false;
    }
}
