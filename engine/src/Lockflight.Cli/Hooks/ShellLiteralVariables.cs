using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// The variables a POSIX command line has set to plain literal text at a point of it (<c>S=/tmp/out; cd $S</c>), so the write
/// scanner can replace <c>$S</c> and <c>${S}</c> by that text. Only assignments known to run in the current shell count: a bare
/// assignment statement or <c>export NAME=value</c> that is not in a pipeline or in the background; one after <c>&amp;&amp;</c>
/// holds until the and-list ends, one after <c>||</c> may not have run. A prefix assignment (<c>NAME=value cmd</c>), a
/// reassignment to anything but literal text and <c>unset</c> make the name unknown again. Compound commands, function
/// definitions, arithmetic and builtins that set variables in other ways (<c>read</c>, <c>eval</c>, <c>source</c>, …) stop the
/// tracking for the rest of the command line: every variable after them is unknown, as it is without tracking. The hook's own
/// environment is never read: a variable the command line did not set is unknown.
/// </summary>
internal sealed partial class ShellLiteralVariables(ShellDialect dialect, StrongBox<bool> untracked)
{
    private const string PlaceholderPrefix = "__";
    private const string FieldSeparator = "IFS";

    private static readonly HashSet<string> CurrentShellSeparators = [";", "\n", "&&", "||"];
    private static readonly HashSet<string> PipelineSeparators = ["|", "|&"];

    /// <summary>Words that start a compound command or a definition, and builtins that set variables other than by NAME=value.</summary>
    private static readonly HashSet<string> Untracking =
    [
        "!", "{", "}", "if", "then", "else", "elif", "fi", "while", "until", "do", "done", "for", "select", "case", "esac", "function",
        "coproc", "eval", "source", ".", "set", "setopt", "unsetopt", "shopt", "emulate", "declare", "typeset", "local", "readonly",
        "integer", "float", "let", "read", "mapfile", "readarray", "getopts", "vared", "zparseopts", "zstat", "strftime", "sysread",
        "zmodload", "alias", "trap",
    ];

    /// <summary>Builtins that assign the variable named after an option: <c>printf -v NAME</c>, <c>print -v NAME</c>, <c>wait -p NAME</c>.</summary>
    private static readonly Dictionary<string, string> VariableOptions = new(StringComparer.Ordinal)
    {
        ["printf"] = "-v",
        ["print"] = "-v",
        ["wait"] = "-p",
    };

    /// <summary>Variables the shell itself sets or reads in a special way: their value is never taken from an assignment.</summary>
    private static readonly HashSet<string> Special =
    [
        "PWD", "OLDPWD", "RANDOM", "SRANDOM", "SECONDS", "LINENO", "BASHPID", "EPOCHSECONDS", "EPOCHREALTIME", "HISTCMD", "PPID", "UID",
        "EUID", "GROUPS", "OPTIND", "OPTARG", "REPLY", "_", "BASHOPTS", "SHELLOPTS", "BASH_VERSINFO", "BASH_SUBSHELL", "BASH_COMMAND",
        "FUNCNAME", "argv", "status", "pipestatus", "ERRNO", "TTYIDLE", FieldSeparator,
    ];

    private Dictionary<string, string> values = new(StringComparer.Ordinal);
    private HashSet<string> untilListEnds = new(StringComparer.Ordinal);

    public ShellLiteralVariables(ShellDialect dialect)
        : this(dialect, new StrongBox<bool>())
    {
    }

    /// <summary>A copy for a subshell or a substitution: what it assigns ends with it, but a stop in tracking does not.</summary>
    public ShellLiteralVariables Copy() =>
        new(dialect, untracked) { values = new(values, StringComparer.Ordinal), untilListEnds = new(untilListEnds, StringComparer.Ordinal) };

    /// <summary>The word with the known variables replaced, and the assignment it makes.</summary>
    public ShellWord Word(ShellToken token)
    {
        if (dialect != ShellDialect.Posix)
        {
            return new ShellWord(token.Text, token);
        }

        if (!token.LiteralDollar && AssigningExpansion().IsMatch(token.Text))
        {
            // ${NAME:=value}, ${NAME::=value} (zsh) and the like assign while they expand.
            Untrack();
        }

        string text = token.LiteralDollar ? token.Text : Expand(token.Text);
        return new ShellWord(text, token) { Assignment = ShellAssignment.Of(token, text) };
    }

    /// <summary>
    /// Applies what a simple command does to the variables. <paramref name="before"/> and <paramref name="after"/> are the
    /// separators around it (null at the start or end of a list); <c>|</c> or <c>&amp;</c> run it outside the current shell.
    /// </summary>
    public void Run(IReadOnlyList<ShellWord> words, string? before, string? after)
    {
        if (dialect != ShellDialect.Posix || words.Count == 0)
        {
            return;
        }

        bool currentShell = (before is null || CurrentShellSeparators.Contains(before) || before == "&")
            && (after is null || CurrentShellSeparators.Contains(after));
        int commandIndex = words.ToList().FindIndex(word => word.Assignment is null);
        if (commandIndex < 0)
        {
            Assign(words, before, currentShell);
            return;
        }

        // A prefix assignment holds for that command only, but may persist before a special builtin or a function.
        foreach (ShellWord prefix in words.Take(commandIndex))
        {
            Forget(prefix.Assignment!.Name);
        }

        if (Untracking.Contains(words[commandIndex].Text))
        {
            Untrack();
            return;
        }

        if (ShellCommand.Parse([.. words.Select(word => word.Text)]) is not ShellCommand command)
        {
            return;
        }

        IReadOnlyList<ShellWord> arguments = [.. words.Skip(words.Count - command.Arguments.Count)];
        RunBuiltin(command.Program, arguments, before, currentShell);
    }

    /// <summary>A separator other than <c>&amp;&amp;</c> ends the and-list: an assignment that may not have run is unknown after it.</summary>
    public void EndList(string separator)
    {
        if (separator == "&&")
        {
            return;
        }

        foreach (string name in untilListEnds)
        {
            values.Remove(name);
        }

        untilListEnds.Clear();
    }

    /// <summary>Stops the tracking for the rest of the command line: every variable is unknown from here on.</summary>
    public void Untrack()
    {
        untracked.Value = true;
        values.Clear();
        untilListEnds.Clear();
    }

    private void RunBuiltin(string program, IReadOnlyList<ShellWord> arguments, string? before, bool currentShell)
    {
        switch (program)
        {
            case "export" when arguments.Any(argument => argument.Text.StartsWith('-')):
                Untrack();
                break;
            case "export":
                Assign([.. arguments.Where(argument => argument.Assignment is not null)], before, currentShell);
                break;
            case "unset":
                foreach (ShellWord argument in arguments.Where(argument => !argument.Text.StartsWith('-')))
                {
                    Forget(argument.Text);
                }

                break;
            default:
                bool setsNamedVariable = VariableOptions.TryGetValue(program, out string? option)
                    && arguments.Any(argument => argument.Text.StartsWith(option, StringComparison.Ordinal));
                if (setsNamedVariable || Untracking.Contains(program))
                {
                    Untrack();
                }

                break;
        }
    }

    /// <summary>
    /// Assignments of one statement, left to right. A value written with an expansion after an earlier assignment of the statement
    /// may refer to it, but was expanded with the earlier value, so it is unknown.
    /// </summary>
    private void Assign(IReadOnlyList<ShellWord> words, string? before, bool currentShell)
    {
        bool sets = currentShell && before != "||" && !untracked.Value;
        bool afterEarlier = false;
        foreach (ShellAssignment assignment in words.Select(word => word.Assignment!))
        {
            if (sets && assignment.Value is string value && !(afterEarlier && assignment.Expanded) && Trackable(assignment.Name))
            {
                Forget(assignment.Name);
                values[assignment.Name] = value;
                if (before == "&&")
                {
                    untilListEnds.Add(assignment.Name);
                }
            }
            else
            {
                Forget(assignment.Name);
            }

            afterEarlier = true;
        }
    }

    /// <summary>Forgets the name and, as zsh ties <c>PATH</c> to <c>path</c>, any name that differs from it in case only.</summary>
    private void Forget(string name)
    {
        if (name == FieldSeparator)
        {
            Untrack();
            return;
        }

        foreach (string known in values.Keys.Where(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            values.Remove(known);
        }
    }

    private static bool Trackable(string name) => !name.StartsWith(PlaceholderPrefix, StringComparison.Ordinal) && !Special.Contains(name);

    /// <summary>
    /// Replaces <c>$NAME</c> and <c>${NAME}</c> of known variables. A reference followed by <c>[</c>, <c>:</c>, <c>(</c> or
    /// <c>{</c> stays: zsh reads a subscript or a modifier there. Other forms (<c>${NAME:-x}</c>, <c>${#NAME}</c>) stay too.
    /// </summary>
    private string Expand(string text)
    {
        if (untracked.Value || values.Count == 0 || !text.Contains('$', StringComparison.Ordinal))
        {
            return text;
        }

        return Reference().Replace(text, match =>
        {
            string name = match.Groups["braced"].Success ? match.Groups["braced"].Value : match.Groups["name"].Value;
            int end = match.Index + match.Length;
            bool modified = end < text.Length && text[end] is '[' or ':' or '(' or '{';
            return name.Length > 0 && !modified && Trackable(name) && values.TryGetValue(name, out string? value) ? value : match.Value;
        });
    }

    [GeneratedRegex(@"\$(\{(?<braced>[A-Za-z_][A-Za-z0-9_]*)\}|\{[^}]*\}|(?<name>[A-Za-z_][A-Za-z0-9_]*)|[0-9@*#?$!-])")]
    private static partial Regex Reference();

    [GeneratedRegex(@"\$\{[^}]*=")]
    private static partial Regex AssigningExpansion();
}
