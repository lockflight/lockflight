using System.Text.RegularExpressions;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// One simple command after its prefixes are peeled off: variable assignments, keywords (<c>if</c>, <c>then</c>, <c>do</c>, <c>!</c>,
/// <c>{</c>, …) and wrappers that run the next word as a program (<c>command</c>, <c>env</c>, <c>sudo</c>, <c>time</c>, <c>nice</c>,
/// <c>exec</c>, <c>timeout</c>, <c>xargs</c>, …). <see cref="Program"/> is the lower-cased file name without <c>.exe</c>. Behind
/// <c>xargs</c> only the arguments on the command line are seen, not those it reads.
/// </summary>
internal sealed partial record ShellCommand(string Program, IReadOnlyList<string> Arguments)
{
    private static readonly HashSet<string> Keywords =
        ["!", "{", "}", "if", "then", "else", "elif", "fi", "do", "done", "while", "until", "esac", "nohup", "noglob"];

    private static readonly Dictionary<string, HashSet<string>> WrapperOptionsWithValue = new()
    {
        ["env"] = ["-u", "--unset", "-C", "--chdir"],
        ["sudo"] = ["-u", "-g", "-h", "-p", "-C", "-D", "-r", "-t", "-U", "-T", "--user", "--group", "--host", "--prompt", "--chdir"],
        ["exec"] = ["-a"],
        ["command"] = [],
        ["builtin"] = [],
        ["time"] = [],
        ["nice"] = ["-n", "--adjustment"],
        ["timeout"] = ["-s", "--signal", "-k", "--kill-after"],
        ["xargs"] = ["-I", "-n", "-P", "-L", "-d", "-E", "-s", "-a", "--arg-file", "--delimiter", "--max-args", "--max-procs", "--replace"],
    };

    /// <summary>Wrappers that take operands of their own before the program: <c>timeout DURATION</c>.</summary>
    private static readonly Dictionary<string, int> WrapperOperands = new() { ["timeout"] = 1 };

    public static ShellCommand? Parse(IReadOnlyList<string> words)
    {
        int index = 0;
        while (index < words.Count)
        {
            string name = ProgramName(words[index]);
            if (Assignment().IsMatch(words[index]) || Keywords.Contains(name))
            {
                index++;
            }
            else if (WrapperOptionsWithValue.TryGetValue(name, out HashSet<string>? optionsWithValue))
            {
                index = SkipWrapper(words, index + 1, optionsWithValue, WrapperOperands.GetValueOrDefault(name));
            }
            else
            {
                break;
            }
        }

        return index < words.Count ? new ShellCommand(ProgramName(words[index]), [.. words.Skip(index + 1)]) : null;
    }

    public static string ProgramName(string word)
    {
        string name = Path.GetFileName(word.Replace('\\', '/')).ToLowerInvariant();
        return name.EndsWith(".exe", StringComparison.Ordinal) ? name[..^4] : name;
    }

    private static int SkipWrapper(IReadOnlyList<string> words, int index, HashSet<string> optionsWithValue, int operands)
    {
        while (index < words.Count && (words[index].StartsWith('-') || Assignment().IsMatch(words[index])))
        {
            index += optionsWithValue.Contains(words[index]) ? 2 : 1;
        }

        return Math.Min(index + operands, words.Count);
    }

    /// <summary>A <c>NAME=value</c> word (also <c>NAME+=</c>, <c>NAME[i]=</c>): it assigns a variable rather than naming a program.</summary>
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(\[[^\]]*\])?\+?=")]
    private static partial Regex Assignment();
}
